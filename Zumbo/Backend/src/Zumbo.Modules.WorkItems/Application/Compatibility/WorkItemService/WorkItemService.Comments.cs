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
    public async Task<WorkItemResponse> AddCommentAsync(string id, AddCommentRequest request, string correlationId, CancellationToken ct)
        => await addCommentHandler.HandleAsync(
            new AddCommentCommand(id, request, correlationId),
            ct);

    public async Task<WorkItemResponse> EditCommentAsync(string id, string commentId, EditCommentRequest request, string correlationId, CancellationToken ct)
        => await editCommentHandler.HandleAsync(
            new EditCommentCommand(id, commentId, request, correlationId),
            ct);

    public async Task<WorkItemResponse> DeleteCommentAsync(string id, string commentId, string correlationId, CancellationToken ct)
        => await deleteCommentHandler.HandleAsync(
            new DeleteCommentCommand(id, commentId, correlationId),
            ct);
}
