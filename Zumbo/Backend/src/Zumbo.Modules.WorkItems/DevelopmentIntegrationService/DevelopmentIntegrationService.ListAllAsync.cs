using System.Security.Cryptography;
using System.Text;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

public sealed partial class DevelopmentIntegrationService{

    private static async Task<List<TDocument>> ListAllAsync<TDocument>(
        IDocumentRepository<TDocument> repository,
        System.Linq.Expressions.Expression<Func<TDocument, bool>> filter,
        CancellationToken ct)
        where TDocument : class, IDocument
    {
        var result = new List<TDocument>();
        string? cursor = null;
        do
        {
            var page = await repository.ListByCursorAsync(filter, cursor, 200, ct);
            result.AddRange(page.Items);
            cursor = page.NextCursor;
        }
        while (cursor is not null);
        return result;
    }

}
