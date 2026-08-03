using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Boards;

public sealed record BoardFilterResponse(
    string? AssigneeUserId,
    string? TeamId,
    IReadOnlyCollection<string> Statuses,
    IReadOnlyCollection<string> Priorities,
    IReadOnlyCollection<string> Labels,
    string? Text);
