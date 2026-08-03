using System.Text.Json;
using Zumbo.BuildingBlocks.Application.Search;

namespace Zumbo.BuildingBlocks.Infrastructure.Search;

public sealed partial class OpenSearchWorkItemSearchIndex
{
    private async Task MigrateLegacyConcreteIndexAsync(CancellationToken cancellationToken)
    {
        await rebuildGate.WaitAsync(cancellationToken);
        var migrationIndex = BuildLegacyMigrationIndexName();
        try
        {
            await EnsureIndexAsync(migrationIndex, cancellationToken);
            var sourceCountBefore = await CountAsync(AliasName, cancellationToken);

            using var reindexRequest = JsonRequest(
                HttpMethod.Post,
                $"{BaseUrl}/_reindex?wait_for_completion=false&refresh=true",
                new
                {
                    source = new { index = AliasName },
                    dest = new { index = migrationIndex }
                });
            using var reindexResponse = await SendAsync(reindexRequest, cancellationToken: cancellationToken);
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

    private string BuildLegacyMigrationIndexName()
    {
        var name = $"{VersionedIndexName}-legacy-{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}-{Guid.NewGuid():N}";
        return name[..Math.Min(name.Length, 255)];
    }

    private static async Task<string> ReadReindexTaskIdAsync(
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

    private async Task WaitForReindexAsync(string taskId, CancellationToken cancellationToken)
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
                    response = await SendAsync(
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

    private static void EnsureReindexTaskSucceeded(JsonElement task)
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

    private async Task ReplaceLegacyConcreteIndexWithAliasAsync(
        string migrationIndex,
        CancellationToken cancellationToken)
    {
        var actions = new object[]
        {
            new { remove_index = new { index = AliasName } },
            new { add = new { index = migrationIndex, alias = AliasName, is_write_index = true } }
        };
        using var request = JsonRequest(HttpMethod.Post, $"{BaseUrl}/_aliases", new { actions });
        using var response = await SendAsync(request, cancellationToken: cancellationToken);
    }
}
