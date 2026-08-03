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

    private static async Task ReplaceOwnedAsync<TDocument>(
        IDocumentRepository<TDocument> repository,
        TDocument document,
        CancellationToken ct)
        where TDocument : class, IWorkItemActivityDocument
    {
        ValidateActivityOwnership(document);
        var result = await repository.ReplaceByVersionAsync(x => x.Id == document.Id, document, document.Version, ct);
        if (!result.Found)
        {
            throw new NotFoundException("WORK_ITEM_ACTIVITY_NOT_FOUND", "Work item activity was not found.");
        }
        document.Version = result.Version!.Value;
    }
}
