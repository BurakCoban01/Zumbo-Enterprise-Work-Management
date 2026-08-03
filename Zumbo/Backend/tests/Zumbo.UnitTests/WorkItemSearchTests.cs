using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Search;
using Zumbo.BuildingBlocks.Infrastructure.Search;

namespace Zumbo.UnitTests;

public sealed class WorkItemSearchTests
{
    [Fact]
    public async Task OpenSearchInitialization_CreatesVersionedIndexAndAliasAfterConcurrentCreate()
    {
        var handler = new ScriptedHandler(
            Reply(HttpStatusCode.NotFound),
            Reply(HttpStatusCode.BadRequest),
            Reply(HttpStatusCode.OK),
            Reply(HttpStatusCode.NotFound),
            Reply(HttpStatusCode.NotFound),
            Reply(HttpStatusCode.OK));
        var index = CreateIndex(handler);

        await index.InitializeAsync();

        Assert.Equal(
            [
                "HEAD /work-items-v1",
                "PUT /work-items-v1",
                "HEAD /work-items-v1",
                "HEAD /_alias/work-items",
                "HEAD /work-items",
                "POST /_aliases"
            ],
            handler.Requests.Select(x => $"{x.Method} {x.Path}"));
        Assert.Contains("mapping_version", handler.Requests[1].Body);
        Assert.Contains("is_write_index", handler.Requests[5].Body);
    }

    [Fact]
    public async Task OpenSearchInitialization_MigratesLegacyConcreteIndexBeforeCreatingAlias()
    {
        var handler = new ScriptedHandler(
            Reply(HttpStatusCode.OK),
            Reply(HttpStatusCode.NotFound),
            Reply(HttpStatusCode.OK),
            Reply(HttpStatusCode.NotFound),
            Reply(HttpStatusCode.OK),
            JsonReply(HttpStatusCode.OK, """{"count":125634}"""),
            JsonReply(HttpStatusCode.OK, """{"task":"node:42"}"""),
            Reply(HttpStatusCode.ServiceUnavailable),
            JsonReply(HttpStatusCode.OK, """{"completed":true,"response":{"failures":[]}}"""),
            JsonReply(HttpStatusCode.OK, """{"count":125634}"""),
            JsonReply(HttpStatusCode.OK, """{"count":125634}"""),
            Reply(HttpStatusCode.OK));
        var index = CreateIndex(handler);

        await index.InitializeAsync();

        Assert.Equal("HEAD /work-items-v1", Request(handler, 0));
        Assert.Equal("HEAD /_alias/work-items", Request(handler, 1));
        Assert.Equal("HEAD /work-items", Request(handler, 2));
        Assert.StartsWith("HEAD /work-items-v1-legacy-", Request(handler, 3), StringComparison.Ordinal);
        Assert.StartsWith("PUT /work-items-v1-legacy-", Request(handler, 4), StringComparison.Ordinal);
        Assert.Equal("GET /work-items/_count", Request(handler, 5));
        Assert.Equal("POST /_reindex", Request(handler, 6));
        Assert.Contains("\"index\":\"work-items\"", handler.Requests[6].Body, StringComparison.Ordinal);
        Assert.StartsWith("GET /_tasks/", Request(handler, 7), StringComparison.Ordinal);
        Assert.StartsWith("GET /_tasks/", Request(handler, 8), StringComparison.Ordinal);
        Assert.Contains("remove_index", handler.Requests[11].Body, StringComparison.Ordinal);
        Assert.Contains("is_write_index", handler.Requests[11].Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task OpenSearchInitialization_PreservesLegacyIndexWhenMigrationCountDiffers()
    {
        var handler = new ScriptedHandler(
            Reply(HttpStatusCode.OK),
            Reply(HttpStatusCode.NotFound),
            Reply(HttpStatusCode.OK),
            Reply(HttpStatusCode.NotFound),
            Reply(HttpStatusCode.OK),
            JsonReply(HttpStatusCode.OK, """{"count":10}"""),
            JsonReply(HttpStatusCode.OK, """{"task":"node:42"}"""),
            JsonReply(HttpStatusCode.OK, """{"completed":true,"response":{"failures":[]}}"""),
            JsonReply(HttpStatusCode.OK, """{"count":9}"""),
            JsonReply(HttpStatusCode.OK, """{"count":10}"""),
            Reply(HttpStatusCode.OK));
        var index = CreateIndex(handler);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => index.InitializeAsync());

        Assert.Contains("count mismatch", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(handler.Requests, request => request.Path == "/_aliases");
        Assert.StartsWith("DELETE /work-items-v1-legacy-", Request(handler, 10), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Search_UsesTenantScopedStructuredDslAndBasicAuthentication()
    {
        var handler = new ScriptedHandler(JsonReply(
            HttpStatusCode.OK,
            """{"hits":{"total":{"value":1},"hits":[{"_id":"item-1"}]}}"""));
        var index = CreateIndex(handler, username: "search-user", password: "secret");

        var result = await index.SearchAsync(new WorkItemSearchQuery(
            "org-1", "project-1", "release notes", "user-1", "Doing", 1, 20, "Bug"));

        Assert.Equal(["item-1"], result.Ids);
        Assert.Equal(1, result.TotalCount);
        var request = Assert.Single(handler.Requests);
        Assert.Equal("Basic", request.AuthorizationScheme);
        Assert.False(string.IsNullOrWhiteSpace(request.AuthorizationParameter));
        Assert.DoesNotContain("query_string", request.Body, StringComparison.Ordinal);
        using var body = JsonDocument.Parse(request.Body);
        Assert.True(body.RootElement.GetProperty("track_total_hits").GetBoolean());
        var filters = body.RootElement.GetProperty("query").GetProperty("bool").GetProperty("filter").ToString();
        Assert.Contains("organizationId", filters);
        Assert.Contains("org-1", filters);
        Assert.Contains("projectId.keyword", filters);
        Assert.Contains("project-1", filters);
    }

    [Fact]
    public void Configuration_RejectsInsecureHttpAndPartialCredentials()
    {
        Assert.Throws<InvalidOperationException>(() =>
            OpenSearchWorkItemSearchIndex.ValidateConfiguration(new OpenSearchOptions
            {
                BaseUrl = "http://opensearch.test",
                IndexName = "work-items"
            }));
        Assert.Throws<InvalidOperationException>(() =>
            OpenSearchWorkItemSearchIndex.ValidateConfiguration(new OpenSearchOptions
            {
                BaseUrl = "https://opensearch.test",
                IndexName = "work-items",
                Username = "user"
            }));
    }

    [Fact]
    public async Task Circuit_StopsRequestsAfterBoundedTransientFailures()
    {
        var handler = new ScriptedHandler(
            Reply(HttpStatusCode.ServiceUnavailable),
            Reply(HttpStatusCode.ServiceUnavailable));
        var index = CreateIndex(handler, circuitFailureThreshold: 2);
        var query = new WorkItemSearchQuery("org-1", "project-1", "failure", null, null);

        await Assert.ThrowsAsync<WorkItemSearchUnavailableException>(() => index.SearchAsync(query));
        await Assert.ThrowsAsync<WorkItemSearchUnavailableException>(() => index.SearchAsync(query));
        var exception = await Assert.ThrowsAsync<WorkItemSearchUnavailableException>(() => index.SearchAsync(query));

        Assert.Contains("circuit", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task Timeout_IsTranslatedToSearchUnavailable()
    {
        var handler = new HangingHandler();
        var index = CreateIndex(handler, requestTimeoutSeconds: 1);

        var exception = await Assert.ThrowsAsync<WorkItemSearchUnavailableException>(() => index.SearchAsync(
            new WorkItemSearchQuery("org-1", "project-1", "slow", null, null)));

        Assert.Contains("timed out", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task Rebuild_ValidatesCountAndAtomicallyCutsAliasOver()
    {
        var handler = new ScriptedHandler(
            Reply(HttpStatusCode.NotFound),
            Reply(HttpStatusCode.OK),
            JsonReply(HttpStatusCode.OK, """{"errors":false}"""),
            JsonReply(HttpStatusCode.OK, """{"count":2}"""),
            JsonReply(HttpStatusCode.OK, """{"work-items-v1":{"aliases":{"work-items":{}}}}"""),
            JsonReply(HttpStatusCode.OK, """{"count":3}"""),
            Reply(HttpStatusCode.OK));
        var index = CreateIndex(handler);
        var records = new[]
        {
            Record("item-1", "org-1", "project-1"),
            Record("item-2", "org-2", "project-2")
        };

        var result = await index.RebuildAsync(records);

        Assert.Equal(2, result.Indexed);
        Assert.Equal(1, result.Removed);
        Assert.StartsWith("work-items-v1-r", result.ActiveIndex, StringComparison.Ordinal);
        Assert.Contains(handler.Requests, x => x.Method == "POST" && x.Path.EndsWith("/_bulk", StringComparison.Ordinal));
        var aliasRequest = handler.Requests.Last();
        Assert.Equal("POST", aliasRequest.Method);
        Assert.Equal("/_aliases", aliasRequest.Path);
        Assert.Contains("remove", aliasRequest.Body);
        Assert.Contains("add", aliasRequest.Body);
    }

    [Fact]
    public async Task RealOpenSearch_TenantIsolationAndAliasRebuild()
    {
        var baseUrl = Environment.GetEnvironmentVariable("ZUMBO_TEST_OPENSEARCH_URL");
        if (string.IsNullOrWhiteSpace(baseUrl)) return;

        var alias = $"zumbo-platform001-{Guid.NewGuid():N}";
        using var client = new HttpClient();
        var index = new OpenSearchWorkItemSearchIndex(
            client,
            Options.Create(new OpenSearchOptions
            {
                BaseUrl = baseUrl,
                IndexName = alias,
                NumberOfReplicas = 0,
                RequestTimeoutSeconds = 30,
                AllowInsecureHttp = baseUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            }));
        try
        {
            await index.InitializeAsync();
            await index.IndexAsync(Record("org-1-item", "org-1", "shared-project"));
            await index.IndexAsync(Record("org-2-item", "org-2", "shared-project"));
            using (var refresh = await client.PostAsync($"{baseUrl}/{alias}/_refresh", null))
                refresh.EnsureSuccessStatusCode();

            var org1 = await index.SearchAsync(new WorkItemSearchQuery(
                "org-1", "shared-project", "org-1-item", null, null));
            Assert.Equal(["org-1-item"], org1.Ids);
            Assert.Equal(1, org1.TotalCount);

            var rebuild = await index.RebuildAsync(
            [
                Record("org-1-rebuilt", "org-1", "shared-project"),
                Record("org-2-rebuilt", "org-2", "shared-project")
            ]);
            Assert.StartsWith($"{alias}-v1-r", rebuild.ActiveIndex, StringComparison.Ordinal);
            var org2 = await index.SearchAsync(new WorkItemSearchQuery(
                "org-2", "shared-project", "org-2-rebuilt", null, null));
            Assert.Equal(["org-2-rebuilt"], org2.Ids);
            Assert.Equal(1, org2.TotalCount);

            using var aliasResponse = await client.GetAsync($"{baseUrl}/_alias/{alias}");
            aliasResponse.EnsureSuccessStatusCode();
            var aliasJson = await aliasResponse.Content.ReadAsStringAsync();
            Assert.Contains(rebuild.ActiveIndex, aliasJson);
        }
        finally
        {
            using var indices = await client.GetAsync($"{baseUrl}/_cat/indices/{alias}*?format=json&h=index");
            if (indices.IsSuccessStatusCode)
            {
                using var json = JsonDocument.Parse(await indices.Content.ReadAsStringAsync());
                foreach (var entry in json.RootElement.EnumerateArray())
                {
                    var name = entry.GetProperty("index").GetString();
                    if (!string.IsNullOrWhiteSpace(name))
                        using (await client.DeleteAsync($"{baseUrl}/{name}")) { }
                }
            }
        }
    }

    private static OpenSearchWorkItemSearchIndex CreateIndex(
        HttpMessageHandler handler,
        string? username = null,
        string? password = null,
        int circuitFailureThreshold = 3,
        int requestTimeoutSeconds = 5) =>
        new(
            new HttpClient(handler),
            Options.Create(new OpenSearchOptions
            {
                BaseUrl = "http://opensearch.test",
                IndexName = "work-items",
                NumberOfReplicas = 0,
                AllowInsecureHttp = true,
                Username = username,
                Password = password,
                CircuitFailureThreshold = circuitFailureThreshold,
                RequestTimeoutSeconds = requestTimeoutSeconds
            }));

    private static WorkItemSearchRecord Record(string id, string organizationId, string projectId) =>
        new(id, projectId, "board-1", id, "description", "To Do", "Medium", null, [], OrganizationId: organizationId);

    private static HttpResponseMessage Reply(HttpStatusCode statusCode) => new(statusCode);

    private static HttpResponseMessage JsonReply(HttpStatusCode statusCode, string json) =>
        new(statusCode) { Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json") };

    private static string Request(ScriptedHandler handler, int index) =>
        $"{handler.Requests[index].Method} {handler.Requests[index].Path}";

    private sealed record CapturedRequest(
        string Method,
        string Path,
        string Body,
        string? AuthorizationScheme,
        string? AuthorizationParameter);

    private sealed class ScriptedHandler(params HttpResponseMessage[] responses) : HttpMessageHandler
    {
        private readonly Queue<HttpResponseMessage> responses = new(responses);
        public List<CapturedRequest> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Requests.Add(new CapturedRequest(
                request.Method.Method,
                request.RequestUri!.AbsolutePath,
                request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken),
                request.Headers.Authorization?.Scheme,
                request.Headers.Authorization?.Parameter));
            return responses.Dequeue();
        }
    }

    private sealed class HangingHandler : HttpMessageHandler
    {
        public int Calls { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Calls++;
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK);
        }
    }
}
