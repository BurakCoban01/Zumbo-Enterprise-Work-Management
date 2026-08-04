using System.Net;
using System.Net.Sockets;
using System.Text;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Runtime;
using Zumbo.Modules.WorkItems;
using Zumbo.Modules.Organizations;
using Zumbo.SharedKernel;

public sealed class PinnedWebhookSender : IWebhookSender, IDisposable
{
    private readonly WebhookTargetPolicy targetPolicy;
    private readonly IOptions<WebhookOptions> options;
    private readonly IExternalDependencyPolicy? resiliencePolicy;
    private readonly PinnedHttpClientPool clientPool = new();

    public PinnedWebhookSender(
        WebhookTargetPolicy targetPolicy,
        IOptions<WebhookOptions> options)
        : this(targetPolicy, options, null)
    {
    }

    public PinnedWebhookSender(
        WebhookTargetPolicy targetPolicy,
        IOptions<WebhookOptions> options,
        IExternalDependencyPolicyProvider? policyProvider)
    {
        this.targetPolicy = targetPolicy;
        this.options = options;
        resiliencePolicy = policyProvider?.Get(ExternalDependencyNames.Webhook);
    }

    public async Task<WebhookSendResult> SendAsync(WebhookSendRequest request, CancellationToken ct)
    {
        if (resiliencePolicy is null)
            return await SendCoreAsync(request, ct);

        try
        {
            return await resiliencePolicy.ExecuteAsync(
                "send",
                ExternalDependencyOperationKind.NonIdempotentWrite,
                token => SendCoreAsync(request, token),
                IsTransient,
                ct);
        }
        catch (ExternalDependencyTimeoutException)
        {
            throw new WebhookDeliveryException("REQUEST_TIMEOUT");
        }
        catch (ExternalDependencyCircuitOpenException)
        {
            throw new WebhookDeliveryException("CIRCUIT_OPEN");
        }
        catch (ExternalDependencyBulkheadRejectedException)
        {
            throw new WebhookDeliveryException("BULKHEAD_SATURATED");
        }
    }

    private async Task<WebhookSendResult> SendCoreAsync(WebhookSendRequest request, CancellationToken ct)
    {
        var target = await targetPolicy.ResolveAsync(request.TargetUrl, ct);
        var timeout = TimeSpan.FromSeconds(Math.Clamp(options.Value.RequestTimeoutSeconds, 1, 30));
        using var clientLease = clientPool.Rent(
            target.Uri,
            target.Addresses,
            timeout,
            () => new WebhookDeliveryException("CONNECT_FAILED"));
        var client = clientLease.Client;
        using var message = new HttpRequestMessage(HttpMethod.Post, target.Uri)
        {
            Content = new StringContent(request.Payload, Encoding.UTF8, "application/json")
        };
        message.Headers.TryAddWithoutValidation("User-Agent", "Zumbo-Webhook/1.0");
        message.Headers.TryAddWithoutValidation("X-Zumbo-Webhook-Id", request.DeliveryId);
        message.Headers.TryAddWithoutValidation("X-Zumbo-Webhook-Timestamp", request.TimestampUnixSeconds.ToString());
        message.Headers.TryAddWithoutValidation("X-Zumbo-Webhook-Signature", "v1=" + request.Signature);
        message.Headers.TryAddWithoutValidation("X-Zumbo-Webhook-Secret-Version", request.SecretVersion.ToString());
        if (request.PreviousSignature is not null && request.PreviousSecretVersion is not null)
        {
            message.Headers.TryAddWithoutValidation("X-Zumbo-Webhook-Previous-Signature", "v1=" + request.PreviousSignature);
            message.Headers.TryAddWithoutValidation(
                "X-Zumbo-Webhook-Previous-Secret-Version",
                request.PreviousSecretVersion.Value.ToString());
        }

        HttpResponseMessage response;
        try
        {
            response = await client.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            throw new WebhookDeliveryException(exception is TaskCanceledException ? "REQUEST_TIMEOUT" : "REQUEST_FAILED");
        }
        using (response)
        {
            var statusCode = (int)response.StatusCode;
            if (statusCode is < 200 or >= 300)
                throw new WebhookDeliveryException($"HTTP_{statusCode}");
            return new WebhookSendResult(statusCode);
        }
    }

    private static bool IsTransient(Exception exception)
    {
        if (exception is not WebhookDeliveryException delivery) return false;
        if (delivery.SafeCode is "REQUEST_TIMEOUT" or "REQUEST_FAILED" or "CONNECT_FAILED") return true;
        if (!delivery.SafeCode.StartsWith("HTTP_", StringComparison.Ordinal)
            || !int.TryParse(delivery.SafeCode[5..], out var statusCode))
        {
            return false;
        }
        return statusCode is 408 or 429 or >= 500;
    }

    public void Dispose() => clientPool.Dispose();
}
