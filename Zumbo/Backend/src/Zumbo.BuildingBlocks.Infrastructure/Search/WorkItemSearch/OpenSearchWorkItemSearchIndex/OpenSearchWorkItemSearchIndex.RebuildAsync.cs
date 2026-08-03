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
}
