using System.Net.Http.Json;
using System.Text.Json;
using Zumbo.BuildingBlocks.Application.Search;

namespace Zumbo.BuildingBlocks.Infrastructure.Search;

internal sealed class OpenSearchResponseParser
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    internal async Task<WorkItemSearchResult> ParseAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        var payload = await response.Content.ReadFromJsonAsync<OpenSearchResponse>(JsonOptions, cancellationToken);
        var ids = payload?.Hits?.Hits?.Select(x => x._id).ToList() ?? [];
        return new WorkItemSearchResult(ids, payload?.Hits?.Total?.Value ?? ids.Count);
    }

    private sealed class OpenSearchHit
    {
        public string _id { get; set; } = string.Empty;
    }

    private sealed class OpenSearchHits
    {
        public List<OpenSearchHit>? Hits { get; set; }
        public OpenSearchTotal? Total { get; set; }
    }

    private sealed class OpenSearchResponse
    {
        public OpenSearchHits? Hits { get; set; }
    }

    private sealed class OpenSearchTotal
    {
        public long Value { get; set; }
    }
}
