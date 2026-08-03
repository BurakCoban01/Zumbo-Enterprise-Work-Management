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

    private static async Task<WorkItemActivityPage<TResponse>> PageAsync<TDocument, TOrder, TResponse>(
        IDocumentRepository<TDocument> repository,
        System.Linq.Expressions.Expression<Func<TDocument, bool>> filter,
        System.Linq.Expressions.Expression<Func<TDocument, TOrder>> orderBy,
        Func<TDocument, TResponse> map,
        int page,
        int pageSize,
        CancellationToken ct)
        where TDocument : class, IDocument
    {
        var normalized = NormalizePage(page, pageSize);
        var items = await repository.ListByFilterAsync(
            filter,
            BoxOrder(orderBy),
            page: normalized.Page,
            pageSize: normalized.PageSize,
            cancellationToken: ct);
        return new(items.Select(map).ToList(), normalized.Page, normalized.PageSize,
            await repository.CountByFilterAsync(filter, ct));
    }
}
