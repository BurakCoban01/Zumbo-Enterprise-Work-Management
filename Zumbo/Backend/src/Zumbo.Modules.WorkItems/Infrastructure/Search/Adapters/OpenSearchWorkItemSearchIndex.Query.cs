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
    private static void AddTermFilter(List<object> filters, string field, string? value)
    {
        OpenSearchQueryClient.AddTermFilter(filters, field, value);
    }

    private static string ExactCustomFieldValue(string key, string value) =>
        OpenSearchQueryClient.ExactCustomFieldValue(key, value);

    private static object KeywordField() => OpenSearchIndexManager.KeywordField();

    private static object SearchableKeywordField() => OpenSearchIndexManager.SearchableKeywordField();

    public async Task<WorkItemSearchResult> SearchAsync(WorkItemSearchQuery query, CancellationToken cancellationToken = default) =>
        await queryClient.SearchAsync(query, cancellationToken);
}
