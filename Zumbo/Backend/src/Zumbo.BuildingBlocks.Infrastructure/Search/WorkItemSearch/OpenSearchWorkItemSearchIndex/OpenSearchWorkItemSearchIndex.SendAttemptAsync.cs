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

    private async Task<HttpResponseMessage> SendAttemptAsync(
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
}
