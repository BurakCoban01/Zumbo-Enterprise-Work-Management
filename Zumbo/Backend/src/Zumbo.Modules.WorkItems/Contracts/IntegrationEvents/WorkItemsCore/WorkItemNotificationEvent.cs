using Zumbo.BuildingBlocks.Application.Search;

namespace Zumbo.Modules.WorkItems;

public sealed record WorkItemNotificationEvent(
    string UserId,
    string Type,
    string Message,
    string DeduplicationKey,
    string? SourceKind = null,
    string? SourceId = null,
    string? ProjectId = null);
