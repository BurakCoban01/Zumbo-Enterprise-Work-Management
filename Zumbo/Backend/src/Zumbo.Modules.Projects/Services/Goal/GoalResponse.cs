using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Projects;

public sealed record GoalResponse(
    string Id,
    string OwnerUserId,
    string Name,
    string? Description,
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    string Status,
    string Health,
    int? Confidence,
    int Progress,
    IReadOnlyCollection<string> ViewerUserIds,
    IReadOnlyCollection<GoalInitiativeLinkResponse> InitiativeLinks,
    IReadOnlyCollection<string> ProjectIds,
    IReadOnlyCollection<KeyResultResponse> KeyResults,
    IReadOnlyCollection<GoalStatusUpdateResponse> StatusUpdates,
    bool CanEdit,
    bool CanUpdateStatus,
    bool Archived,
    DateTimeOffset UpdatedAt,
    long Version,
    int StatusUpdateRetentionLimit = ProjectHistoryRetentionPolicy.MaximumGoalStatusUpdates) : IVersionedResource;
