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
    private async Task<long> CountAsync(string indexName, CancellationToken cancellationToken) =>
        await indexManager.CountAsync(indexName, cancellationToken);

    private static string CreateBulkPayload(IEnumerable<WorkItemSearchRecord> records) =>
        OpenSearchBulkWriter.CreateBulkPayload(records);

    public async Task DeleteAsync(string id, CancellationToken cancellationToken = default) =>
        await bulkWriter.DeleteAsync(id, cancellationToken);

    public async Task IndexAsync(WorkItemSearchRecord record, CancellationToken cancellationToken = default) =>
        await bulkWriter.IndexAsync(record, cancellationToken);

    public async Task<WorkItemSearchRebuildResult> RebuildAsync(
        IReadOnlyCollection<WorkItemSearchRecord> records,
        CancellationToken cancellationToken = default) =>
        await bulkWriter.RebuildAsync(records, cancellationToken);
}
