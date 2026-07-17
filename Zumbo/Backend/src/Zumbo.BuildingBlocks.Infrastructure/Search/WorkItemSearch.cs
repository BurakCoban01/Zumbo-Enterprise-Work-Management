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

public sealed class OpenSearchOptions
{
    public string BaseUrl { get; init; } = string.Empty;
    public string IndexName { get; init; } = "zumbo-work-items";
    public int MappingVersion { get; init; } = 1;
    public int NumberOfShards { get; init; } = 1;
    public int NumberOfReplicas { get; init; } = 1;
    public string? Username { get; init; }
    public string? Password { get; init; }
    public bool AllowInsecureHttp { get; init; }
    public int RequestTimeoutSeconds { get; init; } = 5;
    public int CircuitFailureThreshold { get; init; } = 3;
    public int CircuitBreakSeconds { get; init; } = 30;
    public int MaxReindexItems { get; init; } = 10_000;
}

public sealed class InMemoryWorkItemSearchIndex : IWorkItemSearchIndex
{
    private readonly ConcurrentDictionary<string, WorkItemSearchRecord> _records = new();

    public Task InitializeAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task IndexAsync(WorkItemSearchRecord record, CancellationToken cancellationToken = default)
    {
        ValidateScope(record.OrganizationId, record.ProjectId);
        _records[record.Id] = record;
        return Task.CompletedTask;
    }

    public Task DeleteAsync(string id, CancellationToken cancellationToken = default)
    {
        _records.TryRemove(id, out _);
        return Task.CompletedTask;
    }

    public Task<WorkItemSearchResult> SearchAsync(WorkItemSearchQuery query, CancellationToken cancellationToken = default)
    {
        ValidateScope(query.OrganizationId, query.ProjectId);
        var text = query.Text?.Trim().ToLowerInvariant();
        var page = Math.Max(query.Page, 1);
        var pageSize = Math.Clamp(query.PageSize, 1, 200);
        var matches = _records.Values
            .Where(x => x.OrganizationId == query.OrganizationId && x.ProjectId == query.ProjectId)
            .Where(x => string.IsNullOrEmpty(query.AssigneeUserId) || x.AssigneeUserId == query.AssigneeUserId)
            .Where(x => string.IsNullOrEmpty(query.Status) || x.Status == query.Status)
            .Where(x => string.IsNullOrEmpty(query.IssueType) || x.Type == query.IssueType)
            .Where(x => string.IsNullOrEmpty(query.CustomFieldKey)
                || (x.CustomFieldExactValues ?? []).Contains(
                    ExactCustomFieldValue(query.CustomFieldKey, query.CustomFieldValue ?? string.Empty),
                    StringComparer.Ordinal))
            .Where(x => string.IsNullOrEmpty(text)
                || x.Title.Contains(text, StringComparison.OrdinalIgnoreCase)
                || x.Description.Contains(text, StringComparison.OrdinalIgnoreCase)
                || x.CustomFieldSearchText.Contains(text, StringComparison.OrdinalIgnoreCase)
                || x.Labels.Any(label => label.Contains(text, StringComparison.OrdinalIgnoreCase)))
            .OrderBy(x => x.Title, StringComparer.Ordinal)
            .ThenBy(x => x.Id, StringComparer.Ordinal)
            .ToList();
        var ids = matches
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(x => x.Id)
            .ToList();

        return Task.FromResult(new WorkItemSearchResult(ids, matches.Count));
    }

    public Task<WorkItemSearchRebuildResult> RebuildAsync(
        IReadOnlyCollection<WorkItemSearchRecord> records,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ValidateRebuildRecords(records, int.MaxValue);
        var replacement = records.ToDictionary(x => x.Id, StringComparer.Ordinal);
        var removed = _records.Keys.Count(id => !replacement.ContainsKey(id));
        _records.Clear();
        foreach (var record in replacement.Values) _records[record.Id] = record;
        return Task.FromResult(new WorkItemSearchRebuildResult("in-memory-v1", replacement.Count, removed, true));
    }

    internal static void ValidateRebuildRecords(IReadOnlyCollection<WorkItemSearchRecord> records, int maximum)
    {
        if (records.Count > maximum)
            throw new InvalidOperationException($"Search rebuild exceeds the configured limit of {maximum} records.");
        if (records.Any(x => string.IsNullOrWhiteSpace(x.OrganizationId) || string.IsNullOrWhiteSpace(x.ProjectId)))
            throw new InvalidOperationException("Search rebuild records require organization and project scope.");
        if (records.Select(x => x.Id).Distinct(StringComparer.Ordinal).Count() != records.Count)
            throw new InvalidOperationException("Search rebuild records must have unique ids.");
    }

    private static void ValidateScope(string organizationId, string projectId)
    {
        if (string.IsNullOrWhiteSpace(organizationId) || string.IsNullOrWhiteSpace(projectId))
            throw new ArgumentException("Search queries require organization and project scope.");
    }

    private static string ExactCustomFieldValue(string key, string value) =>
        $"{key.Trim()}\u001f{value.Trim()}";
}

public sealed class OpenSearchWorkItemSearchIndex : IWorkItemSearchIndex
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly HttpClient httpClient;
    private readonly OpenSearchOptions options;
    private readonly IExternalDependencyPolicy? resiliencePolicy;
    private readonly object circuitGate = new();
    private readonly SemaphoreSlim rebuildGate = new(1, 1);
    private int consecutiveFailures;
    private DateTimeOffset circuitOpenUntil;

    public OpenSearchWorkItemSearchIndex(HttpClient httpClient, IOptions<OpenSearchOptions> options)
        : this(httpClient, options, null)
    {
    }

    public OpenSearchWorkItemSearchIndex(
        HttpClient httpClient,
        IOptions<OpenSearchOptions> options,
        IExternalDependencyPolicyProvider? policyProvider)
    {
        this.httpClient = httpClient;
        this.options = options.Value;
        resiliencePolicy = policyProvider?.Get(ExternalDependencyNames.OpenSearch);
    }

    private string AliasName => options.IndexName.Trim();
    private string VersionedIndexName => $"{AliasName}-v{options.MappingVersion}";
    private string BaseUrl => options.BaseUrl.TrimEnd('/');

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        ValidateConfiguration(options);
        await EnsureIndexAsync(VersionedIndexName, cancellationToken);

        using var aliasResponse = await SendAsync(
            new HttpRequestMessage(HttpMethod.Head, $"{BaseUrl}/_alias/{AliasName}"),
            allowNotFound: true,
            cancellationToken);
        if (aliasResponse.StatusCode == HttpStatusCode.NotFound)
        {
            await ChangeAliasAsync([], VersionedIndexName, cancellationToken);
        }
    }

    public async Task IndexAsync(WorkItemSearchRecord record, CancellationToken cancellationToken = default)
    {
        ValidateScope(record.OrganizationId, record.ProjectId);
        using var request = JsonRequest(HttpMethod.Put, $"{BaseUrl}/{AliasName}/_doc/{Uri.EscapeDataString(record.Id)}", record);
        using var response = await SendAsync(request, cancellationToken: cancellationToken);
    }

    public async Task DeleteAsync(string id, CancellationToken cancellationToken = default)
    {
        using var response = await SendAsync(
            new HttpRequestMessage(HttpMethod.Delete, $"{BaseUrl}/{AliasName}/_doc/{Uri.EscapeDataString(id)}"),
            allowNotFound: true,
            cancellationToken);
    }

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

    public async Task<WorkItemSearchRebuildResult> RebuildAsync(
        IReadOnlyCollection<WorkItemSearchRecord> records,
        CancellationToken cancellationToken = default)
    {
        ValidateConfiguration(options);
        InMemoryWorkItemSearchIndex.ValidateRebuildRecords(records, options.MaxReindexItems);
        await rebuildGate.WaitAsync(cancellationToken);
        try
        {
            var revision = $"{VersionedIndexName}-r{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}-{Guid.NewGuid():N}";
            revision = revision[..Math.Min(revision.Length, 255)];
            await EnsureIndexAsync(revision, cancellationToken);
            try
            {
                if (records.Count > 0)
                {
                    using var bulkRequest = new HttpRequestMessage(
                        HttpMethod.Post,
                        $"{BaseUrl}/{revision}/_bulk?refresh=true")
                    {
                        Content = new StringContent(CreateBulkPayload(records), Encoding.UTF8, "application/x-ndjson")
                    };
                    using var bulkResponse = await SendAsync(bulkRequest, cancellationToken: cancellationToken);
                    var bulk = await bulkResponse.Content.ReadFromJsonAsync<OpenSearchBulkResponse>(JsonOptions, cancellationToken);
                    if (bulk?.Errors == true)
                        throw new InvalidOperationException("OpenSearch rebuild bulk indexing reported item failures.");
                }

                var indexed = await CountAsync(revision, cancellationToken);
                if (indexed != records.Count)
                    throw new InvalidOperationException($"OpenSearch rebuild count mismatch: expected {records.Count}, indexed {indexed}.");

                var oldIndexes = await GetAliasIndexesAsync(cancellationToken);
                var previousCount = 0L;
                foreach (var oldIndex in oldIndexes)
                    previousCount += await CountAsync(oldIndex, cancellationToken);
                await ChangeAliasAsync(oldIndexes, revision, cancellationToken);
                return new WorkItemSearchRebuildResult(
                    revision,
                    records.Count,
                    (int)Math.Min(Math.Max(previousCount - records.Count, 0), int.MaxValue),
                    true);
            }
            catch
            {
                await TryDeleteIndexAsync(revision, cancellationToken);
                throw;
            }
        }
        finally
        {
            rebuildGate.Release();
        }
    }

    public static void ValidateConfiguration(OpenSearchOptions options)
    {
        if (!Uri.TryCreate(options.BaseUrl, UriKind.Absolute, out var baseUri)
            || (baseUri.Scheme != Uri.UriSchemeHttp && baseUri.Scheme != Uri.UriSchemeHttps))
            throw new InvalidOperationException("OpenSearch base URL must be an absolute HTTP or HTTPS URL.");
        if (baseUri.Scheme == Uri.UriSchemeHttp && !options.AllowInsecureHttp)
            throw new InvalidOperationException("OpenSearch HTTP requires AllowInsecureHttp=true; use HTTPS otherwise.");
        if (string.IsNullOrWhiteSpace(options.IndexName)
            || options.IndexName.Any(char.IsUpper)
            || options.IndexName.IndexOfAny([' ', '/', '\\', '*', '?', '#', ',']) >= 0)
            throw new InvalidOperationException("OpenSearch index name is invalid.");
        if (options.MappingVersion < 1 || options.NumberOfShards < 1 || options.NumberOfReplicas < 0)
            throw new InvalidOperationException("OpenSearch mapping, shard and replica settings are invalid.");
        if (string.IsNullOrWhiteSpace(options.Username) != string.IsNullOrWhiteSpace(options.Password))
            throw new InvalidOperationException("OpenSearch username and password must be configured together.");
        if (options.RequestTimeoutSeconds is < 1 or > 120
            || options.CircuitFailureThreshold is < 1 or > 20
            || options.CircuitBreakSeconds is < 1 or > 300
            || options.MaxReindexItems is < 1 or > 100_000)
            throw new InvalidOperationException("OpenSearch timeout, circuit and reindex limits are outside supported bounds.");
    }

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

    private async Task<IReadOnlyList<string>> GetAliasIndexesAsync(CancellationToken cancellationToken)
    {
        using var response = await SendAsync(
            new HttpRequestMessage(HttpMethod.Get, $"{BaseUrl}/_alias/{AliasName}"),
            allowNotFound: true,
            cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound) return [];
        using var payload = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
        return payload.RootElement.EnumerateObject().Select(x => x.Name).Order(StringComparer.Ordinal).ToList();
    }

    private async Task ChangeAliasAsync(
        IReadOnlyCollection<string> oldIndexes,
        string newIndex,
        CancellationToken cancellationToken)
    {
        var actions = oldIndexes
            .Where(index => !index.Equals(newIndex, StringComparison.Ordinal))
            .Select(index => (object)new { remove = new { index, alias = AliasName } })
            .Append(new { add = new { index = newIndex, alias = AliasName, is_write_index = true } })
            .ToList();
        using var request = JsonRequest(HttpMethod.Post, $"{BaseUrl}/_aliases", new { actions });
        using var response = await SendAsync(request, cancellationToken: cancellationToken);
    }

    private async Task<long> CountAsync(string indexName, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(
            new HttpRequestMessage(HttpMethod.Get, $"{BaseUrl}/{indexName}/_count"),
            cancellationToken: cancellationToken);
        var count = await response.Content.ReadFromJsonAsync<OpenSearchCountResponse>(JsonOptions, cancellationToken);
        return count?.Count ?? 0;
    }

    private async Task TryDeleteIndexAsync(string indexName, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await SendAsync(
                new HttpRequestMessage(HttpMethod.Delete, $"{BaseUrl}/{indexName}"),
                allowNotFound: true,
                cancellationToken);
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            // The inactive revision can be removed by the next maintenance run.
        }
    }

    private async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        bool allowNotFound = false,
        CancellationToken cancellationToken = default)
    {
        if (resiliencePolicy is not null)
        {
            try
            {
                return await resiliencePolicy.ExecuteAsync(
                    $"http-{request.Method.Method.ToLowerInvariant()}",
                    request.Method is { } method && (method == HttpMethod.Get || method == HttpMethod.Head)
                        ? ExternalDependencyOperationKind.Read
                        : ExternalDependencyOperationKind.IdempotentWrite,
                    async token =>
                    {
                        using var attempt = await CloneAsync(request, token);
                        return await SendAttemptAsync(attempt, allowNotFound, useLocalCircuit: false, token);
                    },
                    exception => exception is WorkItemSearchUnavailableException,
                    cancellationToken);
            }
            catch (WorkItemSearchUnavailableException)
            {
                throw;
            }
            catch (Exception exception) when (exception is ExternalDependencyTimeoutException
                or ExternalDependencyCircuitOpenException
                or ExternalDependencyBulkheadRejectedException)
            {
                throw new WorkItemSearchUnavailableException("OpenSearch resilience policy rejected the request.", exception);
            }
        }

        return await SendAttemptAsync(request, allowNotFound, useLocalCircuit: true, cancellationToken);
    }

    private async Task<HttpResponseMessage> SendAttemptAsync(
        HttpRequestMessage request,
        bool allowNotFound,
        bool useLocalCircuit,
        CancellationToken cancellationToken)
    {
        if (useLocalCircuit)
        {
            ThrowIfCircuitOpen();
        }
        if (!string.IsNullOrWhiteSpace(options.Username))
        {
            var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{options.Username}:{options.Password}"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        if (useLocalCircuit)
            timeout.CancelAfter(TimeSpan.FromSeconds(options.RequestTimeoutSeconds));
        try
        {
            var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            if (IsTransient(response.StatusCode))
            {
                response.Dispose();
                if (useLocalCircuit) RegisterFailure();
                throw new WorkItemSearchUnavailableException($"OpenSearch returned {(int)response.StatusCode}.");
            }
            if (response.StatusCode == HttpStatusCode.NotFound && allowNotFound)
            {
                if (useLocalCircuit) ResetCircuit();
                return response;
            }
            response.EnsureSuccessStatusCode();
            if (useLocalCircuit) ResetCircuit();
            return response;
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            if (useLocalCircuit) RegisterFailure();
            throw new WorkItemSearchUnavailableException("OpenSearch request timed out.", exception);
        }
        catch (HttpRequestException exception) when (exception.StatusCode is null)
        {
            if (useLocalCircuit) RegisterFailure();
            throw new WorkItemSearchUnavailableException("OpenSearch request failed.", exception);
        }
    }

    private static async Task<HttpRequestMessage> CloneAsync(
        HttpRequestMessage source,
        CancellationToken cancellationToken)
    {
        var clone = new HttpRequestMessage(source.Method, source.RequestUri)
        {
            Version = source.Version,
            VersionPolicy = source.VersionPolicy
        };
        foreach (var header in source.Headers)
            clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
        if (source.Content is not null)
        {
            var bytes = await source.Content.ReadAsByteArrayAsync(cancellationToken);
            clone.Content = new ByteArrayContent(bytes);
            foreach (var header in source.Content.Headers)
                clone.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }
        return clone;
    }

    private void ThrowIfCircuitOpen()
    {
        lock (circuitGate)
        {
            if (circuitOpenUntil > DateTimeOffset.UtcNow)
                throw new WorkItemSearchUnavailableException("OpenSearch circuit is open.");
            if (circuitOpenUntil != default)
            {
                circuitOpenUntil = default;
                consecutiveFailures = 0;
            }
        }
    }

    private void RegisterFailure()
    {
        lock (circuitGate)
        {
            consecutiveFailures++;
            if (consecutiveFailures >= options.CircuitFailureThreshold)
                circuitOpenUntil = DateTimeOffset.UtcNow.AddSeconds(options.CircuitBreakSeconds);
        }
    }

    private void ResetCircuit()
    {
        lock (circuitGate)
        {
            consecutiveFailures = 0;
            circuitOpenUntil = default;
        }
    }

    private static bool IsTransient(HttpStatusCode statusCode) =>
        statusCode == HttpStatusCode.RequestTimeout
        || statusCode == HttpStatusCode.TooManyRequests
        || (int)statusCode >= 500;

    private static HttpRequestMessage JsonRequest(HttpMethod method, string url, object body) =>
        new(method, url) { Content = JsonContent.Create(body, options: JsonOptions) };

    private static string CreateBulkPayload(IEnumerable<WorkItemSearchRecord> records)
    {
        var builder = new StringBuilder();
        foreach (var record in records.OrderBy(x => x.Id, StringComparer.Ordinal))
        {
            builder.Append(JsonSerializer.Serialize(new { index = new { _id = record.Id } }, JsonOptions)).Append('\n');
            builder.Append(JsonSerializer.Serialize(record, JsonOptions)).Append('\n');
        }
        return builder.ToString();
    }

    private static void AddTermFilter(List<object> filters, string field, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
            filters.Add(new { term = new Dictionary<string, string> { [field] = value } });
    }

    private static object KeywordField() => new { type = "keyword", ignore_above = 256 };

    private static object SearchableKeywordField() => new
    {
        type = "text",
        fields = new { keyword = KeywordField() }
    };

    private static string ExactCustomFieldValue(string key, string value) =>
        $"{key.Trim()}\u001f{value.Trim()}";

    private static void ValidateScope(string organizationId, string projectId)
    {
        if (string.IsNullOrWhiteSpace(organizationId) || string.IsNullOrWhiteSpace(projectId))
            throw new ArgumentException("OpenSearch operations require organization and project scope.");
    }

    private sealed class OpenSearchResponse
    {
        public OpenSearchHits? Hits { get; set; }
    }

    private sealed class OpenSearchHits
    {
        public List<OpenSearchHit>? Hits { get; set; }
        public OpenSearchTotal? Total { get; set; }
    }

    private sealed class OpenSearchTotal
    {
        public long Value { get; set; }
    }

    private sealed class OpenSearchHit
    {
        [JsonPropertyName("_id")]
        public string Id { get; set; } = string.Empty;
    }

    private sealed class OpenSearchBulkResponse
    {
        public bool Errors { get; set; }
    }

    private sealed class OpenSearchCountResponse
    {
        public long Count { get; set; }
    }
}
