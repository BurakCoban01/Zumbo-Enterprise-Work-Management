using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Runtime;
using Zumbo.BuildingBlocks.Application.Search;

namespace Zumbo.BuildingBlocks.Infrastructure.Search;

public sealed partial class OpenSearchWorkItemSearchIndex {

    private async Task<HttpResponseMessage> SendAsync(
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
}
