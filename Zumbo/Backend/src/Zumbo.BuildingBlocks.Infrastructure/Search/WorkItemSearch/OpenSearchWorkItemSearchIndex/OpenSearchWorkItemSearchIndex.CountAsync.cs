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

    private async Task<long> CountAsync(string indexName, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(
            new HttpRequestMessage(HttpMethod.Get, $"{BaseUrl}/{indexName}/_count"),
            cancellationToken: cancellationToken);
        var count = await response.Content.ReadFromJsonAsync<OpenSearchCountResponse>(JsonOptions, cancellationToken);
        return count?.Count ?? 0;
    }
}
