using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Runtime;
using Zumbo.BuildingBlocks.Application.Search;

namespace Zumbo.BuildingBlocks.Infrastructure.Search;

internal sealed class OpenSearchTransport
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly HttpClient httpClient;
    private readonly OpenSearchOptions options;
    private readonly IExternalDependencyPolicy? resiliencePolicy;
    private readonly object circuitGate = new();
    private int consecutiveFailures;
    private DateTimeOffset circuitOpenUntil;

    internal OpenSearchTransport(
        HttpClient httpClient,
        OpenSearchOptions options,
        IExternalDependencyPolicy? resiliencePolicy)
    {
        this.httpClient = httpClient;
        this.options = options;
        this.resiliencePolicy = resiliencePolicy;
    }

    internal static HttpRequestMessage JsonRequest(HttpMethod method, string url, object body) =>
        new(method, url) { Content = JsonContent.Create(body, options: JsonOptions) };

    internal async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        bool allowNotFound = false,
        CancellationToken cancellationToken = default)
    {
        if (resiliencePolicy is not null)
        {
            try
            {
                return await resiliencePolicy.ExecuteAsync(
                    $"http-{request.Method.Method.ToLowerInvariant()}",
                    request.Method is { } method && (method == HttpMethod.Get || method == HttpMethod.Head)
                        ? ExternalDependencyOperationKind.Read
                        : ExternalDependencyOperationKind.IdempotentWrite,
                    async token =>
                    {
                        using var attempt = await CloneAsync(request, token);
                        return await SendAttemptAsync(attempt, allowNotFound, useLocalCircuit: false, token);
                    },
                    exception => exception is WorkItemSearchUnavailableException,
                    cancellationToken);
            }
            catch (WorkItemSearchUnavailableException)
            {
                throw;
            }
            catch (Exception exception) when (exception is ExternalDependencyTimeoutException
                or ExternalDependencyCircuitOpenException
                or ExternalDependencyBulkheadRejectedException)
            {
                throw new WorkItemSearchUnavailableException("OpenSearch resilience policy rejected the request.", exception);
            }
        }

        return await SendAttemptAsync(request, allowNotFound, useLocalCircuit: true, cancellationToken);
    }

    internal async Task<HttpResponseMessage> SendAttemptAsync(
        HttpRequestMessage request,
        bool allowNotFound,
        bool useLocalCircuit,
        CancellationToken cancellationToken)
    {
        if (useLocalCircuit)
        {
            ThrowIfCircuitOpen();
        }
        if (!string.IsNullOrWhiteSpace(options.Username))
        {
            var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{options.Username}:{options.Password}"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        if (useLocalCircuit)
            timeout.CancelAfter(TimeSpan.FromSeconds(options.RequestTimeoutSeconds));
        try
        {
            var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            if (IsTransient(response.StatusCode))
            {
                response.Dispose();
                if (useLocalCircuit) RegisterFailure();
                throw new WorkItemSearchUnavailableException($"OpenSearch returned {(int)response.StatusCode}.");
            }
            if (response.StatusCode == HttpStatusCode.NotFound && allowNotFound)
            {
                if (useLocalCircuit) ResetCircuit();
                return response;
            }
            response.EnsureSuccessStatusCode();
            if (useLocalCircuit) ResetCircuit();
            return response;
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            if (useLocalCircuit) RegisterFailure();
            throw new WorkItemSearchUnavailableException("OpenSearch request timed out.", exception);
        }
        catch (HttpRequestException exception) when (exception.StatusCode is null)
        {
            if (useLocalCircuit) RegisterFailure();
            throw new WorkItemSearchUnavailableException("OpenSearch request failed.", exception);
        }
    }

    internal static async Task<HttpRequestMessage> CloneAsync(
        HttpRequestMessage source,
        CancellationToken cancellationToken)
    {
        var clone = new HttpRequestMessage(source.Method, source.RequestUri)
        {
            Version = source.Version,
            VersionPolicy = source.VersionPolicy
        };
        foreach (var header in source.Headers)
            clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
        if (source.Content is not null)
        {
            var bytes = await source.Content.ReadAsByteArrayAsync(cancellationToken);
            clone.Content = new ByteArrayContent(bytes);
            foreach (var header in source.Content.Headers)
                clone.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }
        return clone;
    }

    internal static bool IsTransient(HttpStatusCode statusCode) =>
        statusCode == HttpStatusCode.RequestTimeout
        || statusCode == HttpStatusCode.TooManyRequests
        || (int)statusCode >= 500;

    internal void RegisterFailure()
    {
        lock (circuitGate)
        {
            consecutiveFailures++;
            if (consecutiveFailures >= options.CircuitFailureThreshold)
                circuitOpenUntil = DateTimeOffset.UtcNow.AddSeconds(options.CircuitBreakSeconds);
        }
    }

    internal void ResetCircuit()
    {
        lock (circuitGate)
        {
            consecutiveFailures = 0;
            circuitOpenUntil = default;
        }
    }

    internal void ThrowIfCircuitOpen()
    {
        lock (circuitGate)
        {
            if (circuitOpenUntil > DateTimeOffset.UtcNow)
                throw new WorkItemSearchUnavailableException("OpenSearch circuit is open.");
            if (circuitOpenUntil != default)
            {
                circuitOpenUntil = default;
                consecutiveFailures = 0;
            }
        }
    }
}
