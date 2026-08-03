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

    private object IndexDefinition() => new
    {
        settings = new
        {
            number_of_shards = options.NumberOfShards,
            number_of_replicas = options.NumberOfReplicas
        },
        mappings = new
        {
            dynamic = "strict",
            _meta = new { mapping_version = options.MappingVersion },
            properties = new Dictionary<string, object>
            {
                ["id"] = KeywordField(),
                ["organizationId"] = KeywordField(),
                ["projectId"] = SearchableKeywordField(),
                ["boardId"] = SearchableKeywordField(),
                ["title"] = new { type = "text" },
                ["description"] = new { type = "text" },
                ["status"] = SearchableKeywordField(),
                ["priority"] = KeywordField(),
                ["type"] = SearchableKeywordField(),
                ["assigneeUserId"] = SearchableKeywordField(),
                ["labels"] = new { type = "text", fields = new { keyword = KeywordField() } },
                ["customFieldSearchText"] = new { type = "text" },
                ["customFieldExactValues"] = KeywordField()
            }
        }
    };
}
