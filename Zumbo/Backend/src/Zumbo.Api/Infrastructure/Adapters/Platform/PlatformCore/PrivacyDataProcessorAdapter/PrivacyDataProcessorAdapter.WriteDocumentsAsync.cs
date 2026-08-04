using System.Linq.Expressions;
using System.Text;
using System.Text.Json;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.Modules.Audit;
using Zumbo.Modules.Identity;
using Zumbo.Modules.Notifications;
using Zumbo.Modules.Organizations;
using Zumbo.Modules.Projects;
using Zumbo.Modules.Teams;
using Zumbo.Modules.WorkItems;
using Zumbo.SharedKernel;

public sealed partial class PrivacyDataProcessorAdapter{

    private static async Task<long> WriteDocumentsAsync<TDocument>(
        IDocumentRepository<TDocument> repository,
        Expression<Func<TDocument, bool>> filter,
        string category,
        Func<TDocument, IEnumerable<PrivacyDataReference>> select,
        StreamWriter writer,
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
                    await WriteReferenceAsync(writer, category, reference, ct);
                    written++;
                }
            }
            cursor = page.NextCursor;
        }
        while (cursor is not null);
        return written;
    }
}
