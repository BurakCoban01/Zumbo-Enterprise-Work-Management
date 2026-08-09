using System.Linq.Expressions;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.Modules.Identity;
using Zumbo.SharedKernel;

internal static class PrivacyDocumentAccess
{
    internal static async Task<IReadOnlyList<TDocument>> LoadAllAsync<TDocument>(
        IDocumentRepository<TDocument> repository,
        Expression<Func<TDocument, bool>> filter,
        CancellationToken ct)
        where TDocument : class, IDocument
    {
        var result = new List<TDocument>();
        string? cursor = null;
        do
        {
            var page = await repository.ListByCursorAsync(
                filter,
                cursor,
                pageSize: 200,
                cancellationToken: ct);
            result.AddRange(page.Items);
            cursor = page.NextCursor;
        }
        while (cursor is not null);

        return result;
    }

    internal static async Task<long> WriteDocumentsAsync<TDocument>(
        IDocumentRepository<TDocument> repository,
        Expression<Func<TDocument, bool>> filter,
        Func<TDocument, IEnumerable<PrivacyDataReference>> select,
        Func<PrivacyDataReference, Task> writeReferenceAsync,
        CancellationToken ct)
        where TDocument : class, IDocument
    {
        long written = 0;
        string? cursor = null;
        do
        {
            var page = await repository.ListByCursorAsync(filter, cursor, 200, ct);
            foreach (var document in page.Items)
            {
                foreach (var reference in select(document))
                {
                    await writeReferenceAsync(reference);
                    written++;
                }
            }
            cursor = page.NextCursor;
        }
        while (cursor is not null);
        return written;
    }
}
