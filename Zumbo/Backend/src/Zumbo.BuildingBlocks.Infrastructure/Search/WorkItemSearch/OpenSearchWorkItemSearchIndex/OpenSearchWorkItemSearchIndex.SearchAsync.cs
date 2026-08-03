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

    public async Task<WorkItemSearchResult> SearchAsync(WorkItemSearchQuery query, CancellationToken cancellationToken = default)
    {
        ValidateScope(query.OrganizationId, query.ProjectId);
        var must = new List<object>();
        var filter = new List<object>
        {
            new { term = new Dictionary<string, string> { ["organizationId"] = query.OrganizationId } },
            new { term = new Dictionary<string, string> { ["projectId.keyword"] = query.ProjectId } }
        };

        if (string.IsNullOrWhiteSpace(query.Text))
        {
            must.Add(new { match_all = new { } });
        }
        else
        {
            must.Add(new
            {
                multi_match = new
                {
                    query = query.Text.Trim(),
                    fields = new[] { "title^2", "description", "labels", "customFieldSearchText" },
                    @operator = "and"
                }
            });
        }

        AddTermFilter(filter, "assigneeUserId.keyword", query.AssigneeUserId);
        AddTermFilter(filter, "status.keyword", query.Status);
        AddTermFilter(filter, "type.keyword", query.IssueType);
        if (!string.IsNullOrWhiteSpace(query.CustomFieldKey))
        {
            AddTermFilter(
                filter,
                "customFieldExactValues",
                ExactCustomFieldValue(query.CustomFieldKey, query.CustomFieldValue ?? string.Empty));
        }

        var page = Math.Max(query.Page, 1);
        var pageSize = Math.Clamp(query.PageSize, 1, 200);
        var body = new
        {
            from = (page - 1) * pageSize,
            size = pageSize,
            track_total_hits = true,
            _source = false,
            query = new { @bool = new { must, filter } }
        };
        using var request = JsonRequest(HttpMethod.Post, $"{BaseUrl}/{AliasName}/_search", body);
        using var response = await SendAsync(request, cancellationToken: cancellationToken);
        var payload = await response.Content.ReadFromJsonAsync<OpenSearchResponse>(JsonOptions, cancellationToken);
        var ids = payload?.Hits?.Hits?.Select(x => x.Id).ToList() ?? [];
        return new WorkItemSearchResult(ids, payload?.Hits?.Total?.Value ?? ids.Count);
    }
}
