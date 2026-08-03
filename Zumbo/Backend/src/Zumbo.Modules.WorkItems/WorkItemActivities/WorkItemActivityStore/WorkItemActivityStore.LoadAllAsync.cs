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

    private static async Task<IReadOnlyList<TDocument>> LoadAllAsync<TDocument>(
        IDocumentRepository<TDocument> repository,
        System.Linq.Expressions.Expression<Func<TDocument, bool>> filter,
        CancellationToken ct)
        where TDocument : class, IDocument
    {
        var result = new List<TDocument>();
        string? cursor = null;
        do
        {
            var current = await repository.ListByCursorAsync(filter, cursor, 200, ct);
            result.AddRange(current.Items);
            cursor = current.NextCursor;
        }
        while (cursor is not null);
        return result;
    }
}
