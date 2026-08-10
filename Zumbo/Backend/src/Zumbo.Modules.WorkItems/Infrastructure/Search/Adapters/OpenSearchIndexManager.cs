using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Zumbo.BuildingBlocks.Application.Search;

namespace Zumbo.BuildingBlocks.Infrastructure.Search;

internal sealed class OpenSearchIndexManager
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly OpenSearchTransport transport;
    private readonly OpenSearchOptions options;
    private readonly SemaphoreSlim rebuildGate;

    internal OpenSearchIndexManager(
        OpenSearchTransport transport,
        OpenSearchOptions options,
        SemaphoreSlim rebuildGate)
    {
        this.transport = transport;
        this.options = options;
        this.rebuildGate = rebuildGate;
    }

    private string AliasName => options.IndexName.Trim();
    private string VersionedIndexName => $"{AliasName}-v{options.MappingVersion}";
    private string BaseUrl => options.BaseUrl.TrimEnd('/');

    internal async Task ChangeAliasAsync(
        IReadOnlyCollection<string> oldIndexes,
        string newIndex,
        CancellationToken cancellationToken)
    {
        var actions = oldIndexes
            .Where(index => !index.Equals(newIndex, StringComparison.Ordinal))
            .Select(index => (object)new { remove = new { index, alias = AliasName } })
            .Append(new { add = new { index = newIndex, alias = AliasName, is_write_index = true } })
            .ToList();
        using var request = OpenSearchTransport.JsonRequest(HttpMethod.Post, $"{BaseUrl}/_aliases", new { actions });
        using var response = await transport.SendAsync(request, cancellationToken: cancellationToken);
    }

    internal async Task<long> CountAsync(string indexName, CancellationToken cancellationToken)
    {
        using var response = await transport.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, $"{BaseUrl}/{indexName}/_count"),
            cancellationToken: cancellationToken);
        var count = await response.Content.ReadFromJsonAsync<OpenSearchCountResponse>(JsonOptions, cancellationToken);
        return count?.Count ?? 0;
    }

    internal async Task EnsureIndexAsync(string indexName, CancellationToken cancellationToken)
    {
        using var headResponse = await transport.SendAsync(
            new HttpRequestMessage(HttpMethod.Head, $"{BaseUrl}/{indexName}"),
            allowNotFound: true,
            cancellationToken);
        if (headResponse.IsSuccessStatusCode) return;

        using var createRequest = OpenSearchTransport.JsonRequest(HttpMethod.Put, $"{BaseUrl}/{indexName}", IndexDefinition());
        try
        {
            using var createResponse = await transport.SendAsync(createRequest, cancellationToken: cancellationToken);
        }
        catch (HttpRequestException)
        {
            using var confirmResponse = await transport.SendAsync(
                new HttpRequestMessage(HttpMethod.Head, $"{BaseUrl}/{indexName}"),
                allowNotFound: true,
                cancellationToken);
            if (!confirmResponse.IsSuccessStatusCode) throw;
        }
    }

    internal async Task<IReadOnlyList<string>> GetAliasIndexesAsync(CancellationToken cancellationToken)
    {
        using var response = await transport.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, $"{BaseUrl}/_alias/{AliasName}"),
            allowNotFound: true,
            cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound) return [];
        using var payload = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync(cancellationToken),
            cancellationToken: cancellationToken);
        return payload.RootElement.EnumerateObject().Select(x => x.Name).Order(StringComparer.Ordinal).ToList();
    }

    internal object IndexDefinition() => new
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

    internal async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        OpenSearchValidation.ValidateConfiguration(options);
        await EnsureIndexAsync(VersionedIndexName, cancellationToken);

        using var aliasResponse = await transport.SendAsync(
            new HttpRequestMessage(HttpMethod.Head, $"{BaseUrl}/_alias/{AliasName}"),
            allowNotFound: true,
            cancellationToken);
        if (aliasResponse.StatusCode == HttpStatusCode.NotFound)
        {
            using var concreteIndexResponse = await transport.SendAsync(
                new HttpRequestMessage(HttpMethod.Head, $"{BaseUrl}/{AliasName}"),
                allowNotFound: true,
                cancellationToken);
            if (concreteIndexResponse.IsSuccessStatusCode)
            {
                await MigrateLegacyConcreteIndexAsync(cancellationToken);
                return;
            }

            await ChangeAliasAsync([], VersionedIndexName, cancellationToken);
        }
    }

    internal async Task MigrateLegacyConcreteIndexAsync(CancellationToken cancellationToken)
    {
        await rebuildGate.WaitAsync(cancellationToken);
        var migrationIndex = BuildLegacyMigrationIndexName();
        try
        {
            await EnsureIndexAsync(migrationIndex, cancellationToken);
            var sourceCountBefore = await CountAsync(AliasName, cancellationToken);

            using var reindexRequest = OpenSearchTransport.JsonRequest(
                HttpMethod.Post,
                $"{BaseUrl}/_reindex?wait_for_completion=false&refresh=true",
                new
                {
                    source = new { index = AliasName },
                    dest = new { index = migrationIndex }
                });
            using var reindexResponse = await transport.SendAsync(reindexRequest, cancellationToken: cancellationToken);
            var taskId = await ReadReindexTaskIdAsync(reindexResponse, cancellationToken);
            await WaitForReindexAsync(taskId, cancellationToken);

            var migratedCount = await CountAsync(migrationIndex, cancellationToken);
            var sourceCountAfter = await CountAsync(AliasName, cancellationToken);
            if (sourceCountBefore != sourceCountAfter || migratedCount != sourceCountAfter)
            {
                throw new InvalidOperationException(
                    $"OpenSearch legacy index migration count mismatch: " +
                    $"source before {sourceCountBefore}, source after {sourceCountAfter}, migrated {migratedCount}.");
            }

            await ReplaceLegacyConcreteIndexWithAliasAsync(migrationIndex, cancellationToken);
        }
        catch (Exception exception) when (exception is TimeoutException or OperationCanceledException)
        {
            throw;
        }
        catch
        {
            await TryDeleteIndexAsync(migrationIndex, cancellationToken);
            throw;
        }
        finally
        {
            rebuildGate.Release();
        }
    }

    internal string BuildLegacyMigrationIndexName()
    {
        var name = $"{VersionedIndexName}-legacy-{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}-{Guid.NewGuid():N}";
        return name[..Math.Min(name.Length, 255)];
    }

    internal static async Task<string> ReadReindexTaskIdAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        using var payload = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync(cancellationToken),
            cancellationToken: cancellationToken);
        if (!payload.RootElement.TryGetProperty("task", out var task)
            || string.IsNullOrWhiteSpace(task.GetString()))
        {
            throw new InvalidOperationException("OpenSearch legacy index migration did not return a task id.");
        }

        return task.GetString()!;
    }

    internal async Task WaitForReindexAsync(string taskId, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(10));
        try
        {
            while (true)
            {
                HttpResponseMessage response;
                try
                {
                    response = await transport.SendAsync(
                        new HttpRequestMessage(
                            HttpMethod.Get,
                            $"{BaseUrl}/_tasks/{Uri.EscapeDataString(taskId)}"),
                        cancellationToken: timeout.Token);
                }
                catch (WorkItemSearchUnavailableException) when (!timeout.IsCancellationRequested)
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(500), timeout.Token);
                    continue;
                }

                using (response)
                {
                    using var payload = await JsonDocument.ParseAsync(
                        await response.Content.ReadAsStreamAsync(timeout.Token),
                        cancellationToken: timeout.Token);
                    if (!payload.RootElement.GetProperty("completed").GetBoolean())
                    {
                        await Task.Delay(TimeSpan.FromMilliseconds(250), timeout.Token);
                        continue;
                    }

                    EnsureReindexTaskSucceeded(payload.RootElement);
                    return;
                }
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException("OpenSearch legacy index migration exceeded 10 minutes.");
        }
    }

    internal static void EnsureReindexTaskSucceeded(JsonElement task)
    {
        if (task.TryGetProperty("error", out var error))
        {
            throw new InvalidOperationException(
                $"OpenSearch legacy index migration task failed: {error.GetRawText()}");
        }

        if (!task.TryGetProperty("response", out var response)
            || !response.TryGetProperty("failures", out var failures)
            || failures.ValueKind != JsonValueKind.Array
            || failures.GetArrayLength() > 0)
        {
            throw new InvalidOperationException("OpenSearch legacy index migration reported item failures.");
        }
    }

    internal async Task ReplaceLegacyConcreteIndexWithAliasAsync(
        string migrationIndex,
        CancellationToken cancellationToken)
    {
        var actions = new object[]
        {
            new { remove_index = new { index = AliasName } },
            new { add = new { index = migrationIndex, alias = AliasName, is_write_index = true } }
        };
        using var request = OpenSearchTransport.JsonRequest(HttpMethod.Post, $"{BaseUrl}/_aliases", new { actions });
        using var response = await transport.SendAsync(request, cancellationToken: cancellationToken);
    }

    internal async Task TryDeleteIndexAsync(string indexName, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await transport.SendAsync(
                new HttpRequestMessage(HttpMethod.Delete, $"{BaseUrl}/{indexName}"),
                allowNotFound: true,
                cancellationToken);
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            // The inactive revision can be removed by the next maintenance run.
        }
    }

    internal static object KeywordField() => new { type = "keyword", ignore_above = 256 };

    internal static object SearchableKeywordField() => new
    {
        type = "text",
        fields = new { keyword = KeywordField() }
    };

    private sealed class OpenSearchCountResponse
    {
        public long Count { get; set; }
    }
}
