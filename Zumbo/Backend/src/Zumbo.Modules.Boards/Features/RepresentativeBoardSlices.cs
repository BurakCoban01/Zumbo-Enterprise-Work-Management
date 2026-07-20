using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Boards;

public sealed record CreateBoardRequest(string ProjectId, string Name, string Type);

public sealed record BoardResponse(
    string Id,
    string ProjectId,
    string Name,
    string Type,
    string SwimlaneMode,
    IReadOnlyCollection<BoardColumnResponse> Columns,
    IReadOnlyCollection<BoardViewResponse> Views,
    bool Archived = false,
    long Version = 0) : IVersionedResource;

public sealed record BoardColumnResponse(
    string Id,
    string Name,
    string Category,
    int Position,
    int? WipLimit,
    IReadOnlyCollection<string>? StatusNames = null);

public sealed record BoardViewResponse(
    string Id,
    string Name,
    string OwnerUserId,
    bool IsShared,
    string SwimlaneMode,
    BoardFilterResponse Filter);

public sealed record BoardFilterResponse(
    string? AssigneeUserId,
    string? TeamId,
    IReadOnlyCollection<string> Statuses,
    IReadOnlyCollection<string> Priorities,
    IReadOnlyCollection<string> Labels,
    string? Text);

public sealed class CreateBoardValidator
{
    public static void Validate(CreateBoardRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.ProjectId) || string.IsNullOrWhiteSpace(request.Name))
        {
            throw new ValidationException("Project id and board name are required.");
        }
    }
}

public sealed class CreateBoardHandler(BoardService service)
{
    public Task<BoardResponse> HandleAsync(CreateBoardRequest request, string correlationId, CancellationToken ct) =>
        service.CreateAsync(request, correlationId, ct);
}

public sealed record ListBoardsByProjectQuery(string ProjectId, bool Archived);

public sealed class ListBoardsByProjectValidator
{
    public static void Validate(ListBoardsByProjectQuery query) => ArgumentNullException.ThrowIfNull(query);
}

public sealed class ListBoardsByProjectHandler(BoardService service)
{
    public Task<IReadOnlyList<BoardResponse>> HandleAsync(ListBoardsByProjectQuery query, CancellationToken ct)
    {
        ListBoardsByProjectValidator.Validate(query);
        return service.ListByProjectAsync(query.ProjectId, ct, query.Archived);
    }
}
