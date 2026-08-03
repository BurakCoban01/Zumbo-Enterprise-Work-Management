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

    private async Task EnsureIndexAsync(string indexName, CancellationToken cancellationToken)
    {
        using var headResponse = await SendAsync(
            new HttpRequestMessage(HttpMethod.Head, $"{BaseUrl}/{indexName}"),
            allowNotFound: true,
            cancellationToken);
        if (headResponse.IsSuccessStatusCode) return;

        using var createRequest = JsonRequest(HttpMethod.Put, $"{BaseUrl}/{indexName}", IndexDefinition());
        try
        {
            using var createResponse = await SendAsync(createRequest, cancellationToken: cancellationToken);
        }
        catch (HttpRequestException)
        {
            using var confirmResponse = await SendAsync(
                new HttpRequestMessage(HttpMethod.Head, $"{BaseUrl}/{indexName}"),
                allowNotFound: true,
                cancellationToken);
            if (!confirmResponse.IsSuccessStatusCode) throw;
        }
    }
}
