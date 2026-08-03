using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Projects;

public sealed record KeyResultResponse(
    string Id,
    string OwnerUserId,
    string Name,
    string? Description,
    decimal BaselineValue,
    decimal TargetValue,
    decimal CurrentValue,
    string Unit,
    string Direction,
    int Progress,
    int? Confidence,
    IReadOnlyCollection<KeyResultProgressUpdateResponse> ProgressUpdates,
    bool CanUpdate,
    int ProgressUpdateRetentionLimit = ProjectHistoryRetentionPolicy.MaximumKeyResultProgressUpdates);
