using System.Collections.Concurrent;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace Zumbo.BuildingBlocks.Infrastructure.Search;

public sealed class SearchOptions
{
    public string Provider { get; init; } = "InMemory";
}

public sealed class OpenSearchOptions
{
    public string BaseUrl { get; init; } = "http://localhost:9200";
    public string IndexName { get; init; } = "zumbo-work-items";
    public int NumberOfShards { get; init; } = 1;
    public int NumberOfReplicas { get; init; } = 1;
}

public sealed record WorkItemSearchRecord(
    string Id,
    string ProjectId,
    string BoardId,
    string Title,
    string Description,
    string Status,
    string Priority,
    string? AssigneeUserId,
    IReadOnlyCollection<string> Labels);

public sealed record WorkItemSearchQuery(
    string? ProjectId,
    string? Text,
    string? AssigneeUserId,
    string? Status,
    int Page = 1,
    int PageSize = 100);

public interface IWorkItemSearchIndex
{
    Task InitializeAsync(CancellationToken cancellationToken = default);
    Task IndexAsync(WorkItemSearchRecord record, CancellationToken cancellationToken = default);
    Task DeleteAsync(string id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<string>> SearchIdsAsync(WorkItemSearchQuery query, CancellationToken cancellationToken = default);
}

public sealed class InMemoryWorkItemSearchIndex : IWorkItemSearchIndex
{
    private readonly ConcurrentDictionary<string, WorkItemSearchRecord> _records = new();

    public Task InitializeAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task IndexAsync(WorkItemSearchRecord record, CancellationToken cancellationToken = default)
    {
        _records[record.Id] = record;
        return Task.CompletedTask;
    }

    public Task DeleteAsync(string id, CancellationToken cancellationToken = default)
    {
        _records.TryRemove(id, out _);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<string>> SearchIdsAsync(WorkItemSearchQuery query, CancellationToken cancellationToken = default)
    {
        var text = query.Text?.Trim().ToLowerInvariant();
        var page = Math.Max(query.Page, 1);
        var pageSize = Math.Clamp(query.PageSize, 1, 200);
        var ids = _records.Values
            .Where(x => string.IsNullOrEmpty(query.ProjectId) || x.ProjectId == query.ProjectId)
            .Where(x => string.IsNullOrEmpty(query.AssigneeUserId) || x.AssigneeUserId == query.AssigneeUserId)
            .Where(x => string.IsNullOrEmpty(query.Status) || x.Status == query.Status)
            .Where(x => string.IsNullOrEmpty(text)
                || x.Title.ToLowerInvariant().Contains(text)
                || x.Description.ToLowerInvariant().Contains(text)
                || x.Labels.Any(label => label.ToLowerInvariant().Contains(text)))
            .OrderBy(x => x.Title)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(x => x.Id)
            .ToList();

        return Task.FromResult<IReadOnlyList<string>>(ids);
    }
}

public sealed class OpenSearchWorkItemSearchIndex(HttpClient httpClient, IOptions<OpenSearchOptions> options) : IWorkItemSearchIndex
{
    private readonly OpenSearchOptions _options = options.Value;

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_options.BaseUrl) || string.IsNullOrWhiteSpace(_options.IndexName))
        {
            throw new InvalidOperationException("OpenSearch base URL and index name are required.");
        }

        if (_options.NumberOfShards < 1 || _options.NumberOfReplicas < 0)
        {
            throw new InvalidOperationException("OpenSearch shard count must be positive and replica count cannot be negative.");
        }

        var indexUrl = $"{_options.BaseUrl.TrimEnd('/')}/{_options.IndexName}";
        using var headRequest = new HttpRequestMessage(HttpMethod.Head, indexUrl);
        using var headResponse = await httpClient.SendAsync(headRequest, cancellationToken);
        if (headResponse.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            var createBody = new
            {
                settings = new
                {
                    number_of_shards = _options.NumberOfShards,
                    number_of_replicas = _options.NumberOfReplicas
                },
                mappings = new
                {
                    dynamic = "strict",
                    properties = new Dictionary<string, object>
                    {
                        ["id"] = KeywordField(),
                        ["projectId"] = SearchableKeywordField(),
                        ["boardId"] = SearchableKeywordField(),
                        ["title"] = new { type = "text" },
                        ["description"] = new { type = "text" },
                        ["status"] = SearchableKeywordField(),
                        ["priority"] = KeywordField(),
                        ["assigneeUserId"] = SearchableKeywordField(),
                        ["labels"] = new { type = "text", fields = new { keyword = KeywordField() } }
                    }
                }
            };
            using var createResponse = await httpClient.PutAsJsonAsync(indexUrl, createBody, cancellationToken);
            createResponse.EnsureSuccessStatusCode();
            return;
        }

        headResponse.EnsureSuccessStatusCode();
        using var settingsResponse = await httpClient.PutAsJsonAsync(
            $"{indexUrl}/_settings",
            new { index = new { number_of_replicas = _options.NumberOfReplicas } },
            cancellationToken);
        settingsResponse.EnsureSuccessStatusCode();
    }

    public async Task IndexAsync(WorkItemSearchRecord record, CancellationToken cancellationToken = default)
    {
        var url = $"{_options.BaseUrl.TrimEnd('/')}/{_options.IndexName}/_doc/{record.Id}";
        var response = await httpClient.PutAsJsonAsync(url, record, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    public async Task DeleteAsync(string id, CancellationToken cancellationToken = default)
    {
        var url = $"{_options.BaseUrl.TrimEnd('/')}/{_options.IndexName}/_doc/{id}";
        var response = await httpClient.DeleteAsync(url, cancellationToken);
        if (response.StatusCode != System.Net.HttpStatusCode.NotFound)
        {
            response.EnsureSuccessStatusCode();
        }
    }

    public async Task<IReadOnlyList<string>> SearchIdsAsync(WorkItemSearchQuery query, CancellationToken cancellationToken = default)
    {
        var must = new List<object>();
        var filter = new List<object>();

        if (string.IsNullOrWhiteSpace(query.Text))
        {
            must.Add(new { match_all = new { } });
        }
        else
        {
            must.Add(new
            {
                query_string = new
                {
                    query = query.Text,
                    fields = new[] { "title^2", "description", "labels" }
                }
            });
        }

        if (!string.IsNullOrWhiteSpace(query.ProjectId))
        {
            filter.Add(new { term = new Dictionary<string, string> { ["projectId.keyword"] = query.ProjectId } });
        }

        if (!string.IsNullOrWhiteSpace(query.AssigneeUserId))
        {
            filter.Add(new { term = new Dictionary<string, string> { ["assigneeUserId.keyword"] = query.AssigneeUserId } });
        }

        if (!string.IsNullOrWhiteSpace(query.Status))
        {
            filter.Add(new { term = new Dictionary<string, string> { ["status.keyword"] = query.Status } });
        }

        var page = Math.Max(query.Page, 1);
        var pageSize = Math.Clamp(query.PageSize, 1, 200);
        var body = new
        {
            from = (page - 1) * pageSize,
            size = pageSize,
            track_total_hits = false,
            _source = false,
            query = new
            {
                @bool = new { must, filter }
            }
        };
        var url = $"{_options.BaseUrl.TrimEnd('/')}/{_options.IndexName}/_search";
        var response = await httpClient.PostAsJsonAsync(url, body, cancellationToken);
        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<OpenSearchResponse>(cancellationToken: cancellationToken);
        return payload?.Hits?.Hits?.Select(x => x.Id).ToList() ?? [];
    }

    private sealed class OpenSearchResponse
    {
        public OpenSearchHits? Hits { get; set; }
    }

    private sealed class OpenSearchHits
    {
        public List<OpenSearchHit>? Hits { get; set; }
    }

    private sealed class OpenSearchHit
    {
        [JsonPropertyName("_id")]
        public string Id { get; set; } = string.Empty;
    }

    private static object KeywordField() => new { type = "keyword", ignore_above = 256 };

    private static object SearchableKeywordField() => new
    {
        type = "text",
        fields = new { keyword = KeywordField() }
    };
}
