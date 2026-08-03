using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Linq.Expressions;
using System.Runtime.CompilerServices;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

public sealed partial class WorkItemActivityStore{

    private static async IAsyncEnumerable<TDocument> StreamAsync<TDocument>(
        IDocumentRepository<TDocument> repository,
        Expression<Func<TDocument, bool>> filter,
        [EnumeratorCancellation] CancellationToken ct)
        where TDocument : class, IDocument
    {
        string? cursor = null;
        do
        {
            var page = await repository.ListByCursorAsync(filter, cursor, 200, ct);
            foreach (var item in page.Items) yield return item;
            cursor = page.NextCursor;
        }
        while (cursor is not null);
    }
}
