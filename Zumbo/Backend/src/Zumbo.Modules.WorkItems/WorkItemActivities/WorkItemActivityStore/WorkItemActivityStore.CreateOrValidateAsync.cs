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

    private static async Task CreateOrValidateAsync<TDocument>(
        IDocumentRepository<TDocument> repository,
        TDocument document,
        Func<TDocument, bool> compatible,
        CancellationToken ct)
        where TDocument : class, IVersionedDocument
    {
        try
        {
            await repository.CreateAsync(document, ct);
        }
        catch (DocumentConflictException)
        {
            var existing = await repository.SelectAsync(x => x.Id == document.Id, ct);
            if (existing is null || !compatible(existing))
            {
                throw new ConflictException(
                    "WORK_ITEM_ACTIVITY_MIGRATION_CONFLICT",
                    "Legacy work item activity conflicts with an existing activity record.");
            }
        }
    }
}
