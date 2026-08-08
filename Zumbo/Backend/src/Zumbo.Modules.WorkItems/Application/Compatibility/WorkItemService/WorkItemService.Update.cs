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
    public async Task<WorkItemResponse> UpdateAsync(string id, UpdateWorkItemRequest request, string correlationId, CancellationToken ct)
        => await updateWorkItemHandler.HandleAsync(
            new UpdateWorkItemCommand(id, request, correlationId),
            ct);

    public async Task<WorkItemResponse> SetCustomFieldsAsync(
        string id,
        SetWorkItemCustomFieldsRequest request,
        string correlationId,
        CancellationToken ct)
        => await setCustomFieldsHandler.HandleAsync(
            new SetCustomFieldsCommand(id, request, correlationId),
            ct);

    public async Task<WorkItemResponse> ClearAssigneeAsync(
        string id,
        string correlationId,
        CancellationToken ct)
        => await clearAssigneeHandler.HandleAsync(new ClearAssigneeCommand(id, correlationId), ct);

    public async Task<WorkItemResponse> AssignAsync(string id, AssignWorkItemRequest request, string correlationId, CancellationToken ct)
        => await assignWorkItemHandler.HandleAsync(
            new AssignWorkItemCommand(id, request, correlationId),
            ct);

    public async Task<WorkItemResponse> SetTeamAsync(
        string id,
        SetWorkItemTeamRequest request,
        string correlationId,
        CancellationToken ct)
        => await setWorkItemTeamHandler.HandleAsync(
            new SetWorkItemTeamCommand(id, request, correlationId),
            ct);

    public async Task<WorkItemResponse> RequestApprovalAsync(
        string id,
        RequestWorkItemApprovalRequest request,
        string correlationId,
        CancellationToken ct)
        => await requestApprovalHandler.HandleAsync(
            new RequestApprovalCommand(id, request, correlationId),
            ct);

    public async Task<WorkItemResponse> DecideApprovalAsync(
        string id,
        string approvalId,
        DecideWorkItemApprovalRequest request,
        string correlationId,
        CancellationToken ct)
        => await decideApprovalHandler.HandleAsync(
            new DecideApprovalCommand(id, approvalId, request, correlationId),
            ct);
}
