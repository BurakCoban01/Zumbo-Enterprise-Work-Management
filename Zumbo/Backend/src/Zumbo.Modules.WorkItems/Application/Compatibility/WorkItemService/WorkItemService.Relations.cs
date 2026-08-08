using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Linq.Expressions;
using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Runtime;
using Zumbo.BuildingBlocks.Application.Search;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

public sealed partial class WorkItemService
{
    public async Task<WorkItemResponse> AddWorkLogAsync(string id, AddWorkLogRequest request, CancellationToken ct)
        => await addWorkLogHandler.HandleAsync(new AddWorkLogCommand(id, request), ct);

    public async Task<WorkItemResponse> SetParentAsync(
        string id,
        SetWorkItemParentRequest request,
        string correlationId,
        CancellationToken ct)
        => await setParentHandler.HandleAsync(new SetParentCommand(id, request, correlationId), ct);

    public async Task<WorkItemResponse> LinkAsync(
        string id,
        LinkWorkItemRequest request,
        string correlationId,
        CancellationToken ct)
        => await linkWorkItemHandler.HandleAsync(new LinkWorkItemCommand(id, request, correlationId), ct);

    public async Task<WorkItemResponse> UnlinkAsync(
        string id,
        string relatedWorkItemId,
        string relationType,
        string correlationId,
        CancellationToken ct)
        => await unlinkWorkItemHandler.HandleAsync(
            new UnlinkWorkItemCommand(id, relatedWorkItemId, relationType, correlationId),
            ct);
}
