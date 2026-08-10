using Zumbo.BuildingBlocks.Application.Search;

namespace Zumbo.BuildingBlocks.Infrastructure.Search;

internal sealed class OpenSearchQueryClient
{
    private readonly OpenSearchTransport transport;
    private readonly OpenSearchOptions options;
    private readonly OpenSearchResponseParser responseParser;

    internal OpenSearchQueryClient(
        OpenSearchTransport transport,
        OpenSearchOptions options,
        OpenSearchResponseParser responseParser)
    {
        this.transport = transport;
        this.options = options;
        this.responseParser = responseParser;
    }

    internal async Task<WorkItemSearchResult> SearchAsync(
        WorkItemSearchQuery query,
        CancellationToken cancellationToken = default)
    {
        OpenSearchValidation.ValidateScope(query.OrganizationId, query.ProjectId);
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
        var baseUrl = options.BaseUrl.TrimEnd('/');
        var aliasName = options.IndexName.Trim();
        using var request = OpenSearchTransport.JsonRequest(HttpMethod.Post, $"{baseUrl}/{aliasName}/_search", body);
        using var response = await transport.SendAsync(request, cancellationToken: cancellationToken);
        return await responseParser.ParseAsync(response, cancellationToken);
    }

    internal static void AddTermFilter(List<object> filters, string field, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
            filters.Add(new { term = new Dictionary<string, string> { [field] = value } });
    }

    internal static string ExactCustomFieldValue(string key, string value) =>
        $"{key.Trim()}\u001f{value.Trim()}";
}
