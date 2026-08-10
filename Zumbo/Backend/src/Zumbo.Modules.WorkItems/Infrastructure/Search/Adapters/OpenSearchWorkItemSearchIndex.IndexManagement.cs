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
    private async Task ChangeAliasAsync(
        IReadOnlyCollection<string> oldIndexes,
        string newIndex,
        CancellationToken cancellationToken) =>
        await indexManager.ChangeAliasAsync(oldIndexes, newIndex, cancellationToken);

    private static async Task<HttpRequestMessage> CloneAsync(
        HttpRequestMessage source,
        CancellationToken cancellationToken) =>
        await OpenSearchTransport.CloneAsync(source, cancellationToken);

    private async Task EnsureIndexAsync(string indexName, CancellationToken cancellationToken) =>
        await indexManager.EnsureIndexAsync(indexName, cancellationToken);

    private async Task<IReadOnlyList<string>> GetAliasIndexesAsync(CancellationToken cancellationToken) =>
        await indexManager.GetAliasIndexesAsync(cancellationToken);

    private object IndexDefinition() => indexManager.IndexDefinition();

    public async Task InitializeAsync(CancellationToken cancellationToken = default) =>
        await indexManager.InitializeAsync(cancellationToken);

    private async Task MigrateLegacyConcreteIndexAsync(CancellationToken cancellationToken) =>
        await indexManager.MigrateLegacyConcreteIndexAsync(cancellationToken);

    private string BuildLegacyMigrationIndexName() => indexManager.BuildLegacyMigrationIndexName();

    private static async Task<string> ReadReindexTaskIdAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken) =>
        await OpenSearchIndexManager.ReadReindexTaskIdAsync(response, cancellationToken);

    private async Task WaitForReindexAsync(string taskId, CancellationToken cancellationToken) =>
        await indexManager.WaitForReindexAsync(taskId, cancellationToken);

    private static void EnsureReindexTaskSucceeded(JsonElement task) =>
        OpenSearchIndexManager.EnsureReindexTaskSucceeded(task);

    private async Task ReplaceLegacyConcreteIndexWithAliasAsync(
        string migrationIndex,
        CancellationToken cancellationToken) =>
        await indexManager.ReplaceLegacyConcreteIndexWithAliasAsync(migrationIndex, cancellationToken);

    private async Task TryDeleteIndexAsync(string indexName, CancellationToken cancellationToken) =>
        await indexManager.TryDeleteIndexAsync(indexName, cancellationToken);
}
