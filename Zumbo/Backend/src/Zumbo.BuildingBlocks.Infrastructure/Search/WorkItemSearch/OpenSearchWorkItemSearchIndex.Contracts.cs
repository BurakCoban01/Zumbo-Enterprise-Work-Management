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
    private sealed class OpenSearchBulkResponse
    {
        public bool Errors { get; set; }
    }

    private sealed class OpenSearchCountResponse
    {
        public long Count { get; set; }
    }

    private sealed class OpenSearchHit
    {
        [JsonPropertyName("_id")]
        public string Id { get; set; } = string.Empty;
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
