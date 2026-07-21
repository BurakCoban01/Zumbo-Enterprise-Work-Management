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

public sealed class WebhookSecretProtectorAdapter(IDataProtectionProvider provider) : IWebhookSecretProtector
{
    private readonly IDataProtector protector = provider.CreateProtector("Zumbo.WorkItems.WebhookSecret.v1");

    public string Protect(string value) => protector.Protect(value);
    public string Unprotect(string value) => protector.Unprotect(value);
}

public sealed class WebhookTargetPolicy(IOptions<WebhookOptions> options) : IWebhookTargetPolicy
{
    public async Task ValidateAsync(string targetUrl, CancellationToken ct)
    {
        _ = await ResolveAsync(targetUrl, ct);
    }

    internal async Task<WebhookResolvedTarget> ResolveAsync(string targetUrl, CancellationToken ct)
    {
        if (!Uri.TryCreate(targetUrl, UriKind.Absolute, out var uri)
            || !string.IsNullOrEmpty(uri.UserInfo)
            || !string.IsNullOrEmpty(uri.Fragment)
            || string.IsNullOrWhiteSpace(uri.Host))
        {
            throw new ValidationException("Webhook target URL is not valid.");
        }

        var allowLoopback = options.Value.AllowHttpLoopback;
        var isHttps = uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase);
        var isHttp = uri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase);
        if (!isHttps && !(allowLoopback && isHttp))
        {
            throw new ValidationException("Webhook target URL must use HTTPS.");
        }

        IPAddress[] addresses;
        try
        {
            addresses = await Dns.GetHostAddressesAsync(uri.DnsSafeHost, ct);
        }
        catch (Exception exception) when (exception is SocketException or ArgumentException)
        {
            throw new ValidationException("Webhook target host could not be resolved.");
        }

        if (addresses.Length == 0)
            throw new ValidationException("Webhook target host could not be resolved.");
        if (isHttp && addresses.Any(address => !IsLoopback(address)))
            throw new ValidationException("Plain HTTP webhook targets are restricted to loopback addresses.");
        if (addresses.Any(address => !IsAllowed(address, allowLoopback)))
            throw new ValidationException("Webhook target resolves to a prohibited network address.");
        return new WebhookResolvedTarget(uri, addresses);
    }

    private static bool IsAllowed(IPAddress address, bool allowLoopback)
    {
        if (IsLoopback(address)) return allowLoopback;
        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
        var bytes = address.GetAddressBytes();
        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            return bytes[0] != 0
                && bytes[0] != 10
                && bytes[0] != 127
                && !(bytes[0] == 169 && bytes[1] == 254)
                && !(bytes[0] == 172 && bytes[1] is >= 16 and <= 31)
                && !(bytes[0] == 100 && bytes[1] is >= 64 and <= 127)
                && !(bytes[0] == 192 && bytes[1] == 0 && bytes[2] is 0 or 2)
                && !(bytes[0] == 192 && bytes[1] == 168)
                && !(bytes[0] == 198 && bytes[1] is 18 or 19)
                && !(bytes[0] == 198 && bytes[1] == 51 && bytes[2] == 100)
                && !(bytes[0] == 203 && bytes[1] == 0 && bytes[2] == 113)
                && !(bytes[0] >= 224);
        }

        return !address.IsIPv6LinkLocal
            && !address.IsIPv6Multicast
            && !address.IsIPv6SiteLocal
            && !(bytes[0] is 0xfc or 0xfd)
            && !(bytes[0] == 0x20 && bytes[1] == 0x01 && bytes[2] == 0x0d && bytes[3] == 0xb8)
            && !address.Equals(IPAddress.IPv6None)
            && !address.Equals(IPAddress.IPv6Any);
    }

    private static bool IsLoopback(IPAddress address) =>
        IPAddress.IsLoopback(address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address);
}

internal sealed record WebhookResolvedTarget(Uri Uri, IReadOnlyList<IPAddress> Addresses);

public sealed class PinnedWebhookSender : IWebhookSender
{
    private readonly WebhookTargetPolicy targetPolicy;
    private readonly IOptions<WebhookOptions> options;
    private readonly IExternalDependencyPolicy? resiliencePolicy;

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
        using var handler = new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            AutomaticDecompression = DecompressionMethods.None,
            UseCookies = false,
            UseProxy = false,
            ConnectCallback = async (_, cancellationToken) =>
            {
                foreach (var address in target.Addresses)
                {
                    var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp)
                    {
                        NoDelay = true
                    };
                    try
                    {
                        await socket.ConnectAsync(address, target.Uri.Port, cancellationToken);
                        return new NetworkStream(socket, ownsSocket: true);
                    }
                    catch (Exception exception) when (exception is SocketException or OperationCanceledException)
                    {
                        socket.Dispose();
                        if (exception is OperationCanceledException) throw;
                    }
                }
                throw new WebhookDeliveryException("CONNECT_FAILED");
            }
        };
        using var client = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(Math.Clamp(options.Value.RequestTimeoutSeconds, 1, 30))
        };
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
}

public sealed class WorkItemWebhookDeliveryAdapter(WorkItemWebhookService service) : IWorkItemWebhookDelivery
{
    public Task DeliverAsync(WorkItemWebhookEvent message, CancellationToken cancellationToken) =>
        throw new InvalidOperationException("Webhook delivery metadata is required.");

    public Task DeliverAsync(
        string sourceEventId,
        string organizationId,
        WorkItemWebhookEvent message,
        CancellationToken cancellationToken) =>
        service.QueueAsync(sourceEventId, organizationId, message, cancellationToken);
}

public sealed class WebhookAuthorizationAdapter(
    IDocumentRepository<OrganizationDocument> organizations,
    ICurrentUser currentUser) : IWebhookAuthorization
{
    public async Task EnsureCanManageAsync(string organizationId, CancellationToken ct)
    {
        var userId = currentUser.UserId
            ?? throw new UnauthorizedException("Authenticated user is required.");
        if (!string.Equals(currentUser.OrganizationId, organizationId, StringComparison.Ordinal))
            throw new ForbiddenException("Webhook management is restricted to the active tenant.");
        if (currentUser.Roles.Contains("SystemAdmin", StringComparer.OrdinalIgnoreCase)
            || currentUser.Roles.Contains("OrganizationAdmin", StringComparer.OrdinalIgnoreCase))
        {
            return;
        }

        var organization = await organizations.SelectAsync(
            x => x.Id == organizationId || x.TenantKey == organizationId,
            ct);
        if (organization is null
            || !string.Equals(organization.OwnerUserId, userId, StringComparison.Ordinal))
        {
            throw new ForbiddenException("Organization owner or administrator permission is required.");
        }
    }
}

public sealed class WebhookDispatcherHostedService(
    IServiceScopeFactory scopeFactory,
    IOptions<WebhookOptions> options,
    ILogger<WebhookDispatcherHostedService> logger) : BackgroundService
{
    private readonly string workerId = $"{Environment.MachineName}:{Environment.ProcessId}:{Guid.NewGuid():N}";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.Enabled) return;
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(
            Math.Clamp(options.Value.DispatcherIntervalSeconds, 1, 3600)));
        do
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var service = scope.ServiceProvider.GetRequiredService<WorkItemWebhookService>();
                await service.DispatchAsync(
                    Math.Clamp(options.Value.DispatchBatchSize, 1, 100),
                    workerId,
                    stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Webhook dispatcher iteration failed.");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
