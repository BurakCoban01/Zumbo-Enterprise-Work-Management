using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Boards;
public sealed record BoardFilterRequest(
    string? AssigneeUserId,
    string? TeamId,
    IReadOnlyCollection<string>? Statuses,
    IReadOnlyCollection<string>? Priorities,
    IReadOnlyCollection<string>? Labels,
    string? Text);
