using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Zumbo.BuildingBlocks.Application.Search;

namespace Zumbo.BuildingBlocks.Infrastructure.Search;

internal sealed class OpenSearchBulkWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly OpenSearchTransport transport;
    private readonly OpenSearchOptions options;
    private readonly OpenSearchIndexManager indexManager;
    private readonly SemaphoreSlim rebuildGate;

    internal OpenSearchBulkWriter(
        OpenSearchTransport transport,
        OpenSearchOptions options,
        OpenSearchIndexManager indexManager,
        SemaphoreSlim rebuildGate)
    {
        this.transport = transport;
        this.options = options;
        this.indexManager = indexManager;
        this.rebuildGate = rebuildGate;
    }

    private string AliasName => options.IndexName.Trim();
    private string VersionedIndexName => $"{AliasName}-v{options.MappingVersion}";
    private string BaseUrl => options.BaseUrl.TrimEnd('/');

    internal static string CreateBulkPayload(IEnumerable<WorkItemSearchRecord> records)
    {
        var builder = new StringBuilder();
        foreach (var record in records.OrderBy(x => x.Id, StringComparer.Ordinal))
        {
            builder.Append(JsonSerializer.Serialize(new { index = new { _id = record.Id } }, JsonOptions)).Append('\n');
            builder.Append(JsonSerializer.Serialize(record, JsonOptions)).Append('\n');
        }
        return builder.ToString();
    }

    internal async Task DeleteAsync(string id, CancellationToken cancellationToken = default)
    {
        using var response = await transport.SendAsync(
            new HttpRequestMessage(HttpMethod.Delete, $"{BaseUrl}/{AliasName}/_doc/{Uri.EscapeDataString(id)}"),
            allowNotFound: true,
            cancellationToken);
    }

    internal async Task IndexAsync(WorkItemSearchRecord record, CancellationToken cancellationToken = default)
    {
        OpenSearchValidation.ValidateScope(record.OrganizationId, record.ProjectId);
        using var request = OpenSearchTransport.JsonRequest(
            HttpMethod.Put,
            $"{BaseUrl}/{AliasName}/_doc/{Uri.EscapeDataString(record.Id)}",
            record);
        using var response = await transport.SendAsync(request, cancellationToken: cancellationToken);
    }

    internal async Task<WorkItemSearchRebuildResult> RebuildAsync(
        IReadOnlyCollection<WorkItemSearchRecord> records,
        CancellationToken cancellationToken = default)
    {
        OpenSearchValidation.ValidateConfiguration(options);
        InMemoryWorkItemSearchIndex.ValidateRebuildRecords(records, options.MaxReindexItems);
        await rebuildGate.WaitAsync(cancellationToken);
        try
        {
            var revision = $"{VersionedIndexName}-r{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}-{Guid.NewGuid():N}";
            revision = revision[..Math.Min(revision.Length, 255)];
            await indexManager.EnsureIndexAsync(revision, cancellationToken);
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
                    using var bulkResponse = await transport.SendAsync(bulkRequest, cancellationToken: cancellationToken);
                    var bulk = await bulkResponse.Content.ReadFromJsonAsync<OpenSearchBulkResponse>(JsonOptions, cancellationToken);
                    if (bulk?.Errors == true)
                        throw new InvalidOperationException("OpenSearch rebuild bulk indexing reported item failures.");
                }

                var indexed = await indexManager.CountAsync(revision, cancellationToken);
                if (indexed != records.Count)
                    throw new InvalidOperationException($"OpenSearch rebuild count mismatch: expected {records.Count}, indexed {indexed}.");

                var oldIndexes = await indexManager.GetAliasIndexesAsync(cancellationToken);
                var previousCount = 0L;
                foreach (var oldIndex in oldIndexes)
                    previousCount += await indexManager.CountAsync(oldIndex, cancellationToken);
                await indexManager.ChangeAliasAsync(oldIndexes, revision, cancellationToken);
                return new WorkItemSearchRebuildResult(
                    revision,
                    records.Count,
                    (int)Math.Min(Math.Max(previousCount - records.Count, 0), int.MaxValue),
                    true);
            }
            catch
            {
                await indexManager.TryDeleteIndexAsync(revision, cancellationToken);
                throw;
            }
        }
        finally
        {
            rebuildGate.Release();
        }
    }
    private sealed class OpenSearchBulkResponse
    {
        public bool Errors { get; set; }
    }
}
