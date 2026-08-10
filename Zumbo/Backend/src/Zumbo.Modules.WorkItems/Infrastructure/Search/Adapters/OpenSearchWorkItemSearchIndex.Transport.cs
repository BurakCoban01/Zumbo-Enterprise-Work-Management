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

public sealed partial class OpenSearchWorkItemSearchIndex
{
    private static HttpRequestMessage JsonRequest(HttpMethod method, string url, object body) =>
        OpenSearchTransport.JsonRequest(method, url, body);

    private async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        bool allowNotFound = false,
        CancellationToken cancellationToken = default) =>
        await transport.SendAsync(request, allowNotFound, cancellationToken);

    private async Task<HttpResponseMessage> SendAttemptAsync(
        HttpRequestMessage request,
        bool allowNotFound,
        bool useLocalCircuit,
        CancellationToken cancellationToken) =>
        await transport.SendAttemptAsync(request, allowNotFound, useLocalCircuit, cancellationToken);
}
