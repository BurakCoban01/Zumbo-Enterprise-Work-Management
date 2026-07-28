using Zumbo.BuildingBlocks.Application.Persistence;

namespace Zumbo.Modules.Projects;

public sealed class GoalDocument : IVersionedDocument
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string OrganizationId { get; set; } = string.Empty;
    public string OwnerUserId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public DateTimeOffset PeriodStartAtUtc { get; set; }
    public DateTimeOffset PeriodEndAtUtc { get; set; }
    public string Status { get; set; } = GoalStatuses.Draft;
    public string Health { get; set; } = GoalHealth.NoUpdate;
    public int? Confidence { get; set; }
    public List<string> ViewerUserIds { get; set; } = [];
    public List<GoalInitiativeLinkDocument> InitiativeLinks { get; set; } = [];
    public List<string> ProjectIds { get; set; } = [];
    public List<KeyResultDocument> KeyResults { get; set; } = [];
    public List<GoalStatusUpdateDocument> StatusUpdates { get; set; } = [];
    public bool Archived { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public long Version { get; set; }
}

public sealed class GoalInitiativeLinkDocument
{
    public string PortfolioId { get; set; } = string.Empty;
    public string InitiativeId { get; set; } = string.Empty;
}

public sealed class KeyResultDocument
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string OwnerUserId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public decimal BaselineValue { get; set; }
    public decimal TargetValue { get; set; }
    public decimal CurrentValue { get; set; }
    public string Unit { get; set; } = string.Empty;
    public string Direction { get; set; } = KeyResultDirections.Increase;
    public int? Confidence { get; set; }
    public List<KeyResultProgressUpdateDocument> ProgressUpdates { get; set; } = [];
}

public sealed class KeyResultProgressUpdateDocument
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public decimal PreviousValue { get; set; }
    public decimal CurrentValue { get; set; }
    public int? Confidence { get; set; }
    public string Note { get; set; } = string.Empty;
    public string AuthorUserId { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class GoalStatusUpdateDocument
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Status { get; set; } = GoalStatuses.Draft;
    public string Health { get; set; } = GoalHealth.NoUpdate;
    public int? Confidence { get; set; }
    public string Note { get; set; } = string.Empty;
    public string AuthorUserId { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
}

public static class GoalStatuses
{
    public const string Draft = "Draft";
    public const string Active = "Active";
    public const string Paused = "Paused";
    public const string Completed = "Completed";
    public const string Cancelled = "Cancelled";

    public static IReadOnlySet<string> Allowed { get; } = new HashSet<string>(
        [Draft, Active, Paused, Completed, Cancelled],
        StringComparer.Ordinal);
}

public static class GoalHealth
{
    public const string NoUpdate = "NoUpdate";
    public const string OnTrack = "OnTrack";
    public const string AtRisk = "AtRisk";
    public const string OffTrack = "OffTrack";

    public static IReadOnlySet<string> Allowed { get; } = new HashSet<string>(
        [NoUpdate, OnTrack, AtRisk, OffTrack],
        StringComparer.Ordinal);
}

public static class KeyResultDirections
{
    public const string Increase = "Increase";
    public const string Decrease = "Decrease";

    public static IReadOnlySet<string> Allowed { get; } = new HashSet<string>(
        [Increase, Decrease],
        StringComparer.Ordinal);
}

public static class GoalSourceStatuses
{
    public const string Ready = "Ready";
    public const string Partial = "Partial";
}
