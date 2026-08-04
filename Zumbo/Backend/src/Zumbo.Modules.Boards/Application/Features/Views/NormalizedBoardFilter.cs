namespace Zumbo.Modules.Boards.Application.Features.Views;

public sealed record NormalizedBoardFilter(
    string? AssigneeUserId,
    string? TeamId,
    IReadOnlyList<string> Statuses,
    IReadOnlyList<string> Priorities,
    IReadOnlyList<string> Labels,
    string? Text);
