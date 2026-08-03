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

    private static async Task CreateOwnedAsync<TDocument>(
        IDocumentRepository<TDocument> repository,
        TDocument document,
        CancellationToken ct)
        where TDocument : class, IWorkItemActivityDocument
    {
        ValidateActivityOwnership(document);
        await repository.CreateAsync(document, ct);
    }
}
