using Zumbo.BuildingBlocks.Application.Persistence;

namespace Zumbo.Modules.Projects;

public sealed class PortfolioDocument : IVersionedDocument
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string OrganizationId { get; set; } = string.Empty;
    public string OwnerUserId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public List<string> ViewerUserIds { get; set; } = [];
    public List<InitiativeDocument> Initiatives { get; set; } = [];
    public List<PortfolioProjectDependencyDocument> Dependencies { get; set; } = [];
    public bool Archived { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public long Version { get; set; }
}

public sealed class InitiativeDocument
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = string.Empty;
    public string? Summary { get; set; }
    public string? ParentInitiativeId { get; set; }
    public string OwnerUserId { get; set; } = string.Empty;
    public string Status { get; set; } = InitiativeStatuses.Planned;
    public string Health { get; set; } = InitiativeHealth.NoUpdate;
    public int? Confidence { get; set; }
    public DateTimeOffset? TargetAt { get; set; }
    public List<string> ProjectIds { get; set; } = [];
    public List<PortfolioMilestoneLinkDocument> MilestoneLinks { get; set; } = [];
    public List<InitiativeStatusUpdateDocument> StatusUpdates { get; set; } = [];
}

public sealed class InitiativeStatusUpdateDocument
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Status { get; set; } = InitiativeStatuses.Planned;
    public string Health { get; set; } = InitiativeHealth.NoUpdate;
    public int? Confidence { get; set; }
    public string Note { get; set; } = string.Empty;
    public string AuthorUserId { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class PortfolioMilestoneLinkDocument
{
    public string ProjectId { get; set; } = string.Empty;
    public string MilestoneId { get; set; } = string.Empty;
}

public sealed class PortfolioProjectDependencyDocument
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string SourceProjectId { get; set; } = string.Empty;
    public string TargetProjectId { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Status { get; set; } = PortfolioDependencyStatuses.Active;
    public DateTimeOffset? RequiredBy { get; set; }
}

public static class InitiativeStatuses
{
    public const string Planned = "Planned";
    public const string Active = "Active";
    public const string Paused = "Paused";
    public const string Completed = "Completed";
    public const string Cancelled = "Cancelled";

    public static IReadOnlySet<string> Allowed { get; } = new HashSet<string>(
        [Planned, Active, Paused, Completed, Cancelled],
        StringComparer.Ordinal);
}

public static class InitiativeHealth
{
    public const string NoUpdate = "NoUpdate";
    public const string OnTrack = "OnTrack";
    public const string AtRisk = "AtRisk";
    public const string OffTrack = "OffTrack";

    public static IReadOnlySet<string> Allowed { get; } = new HashSet<string>(
        [NoUpdate, OnTrack, AtRisk, OffTrack],
        StringComparer.Ordinal);
}

public static class PortfolioDependencyStatuses
{
    public const string Active = "Active";
    public const string Resolved = "Resolved";

    public static IReadOnlySet<string> Allowed { get; } = new HashSet<string>(
        [Active, Resolved],
        StringComparer.Ordinal);
}

public static class PortfolioSourceStatuses
{
    public const string Ready = "Ready";
    public const string Partial = "Partial";
}
