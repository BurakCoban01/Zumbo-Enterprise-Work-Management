using Zumbo.BuildingBlocks.Application.Persistence;

namespace Zumbo.Modules.WorkItems;

public sealed class DashboardDocument : IVersionedDocument
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string OrganizationId { get; set; } = string.Empty;
    public string OwnerUserId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string Scope { get; set; } = DashboardScopes.Personal;
    public List<string> ProjectIds { get; set; } = [];
    public List<DashboardWidgetDocument> Widgets { get; set; } = [];
    public DashboardFilterDocument Filter { get; set; } = new();
    public List<string> ViewerUserIds { get; set; } = [];
    public bool Archived { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public long Version { get; set; }
}

public sealed class DashboardWidgetDocument
{
    public string Id { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public int Column { get; set; }
    public int Row { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public string? ProjectId { get; set; }
    public DashboardFilterDocument? Filter { get; set; }
}

public sealed class DashboardFilterDocument
{
    public int RangeDays { get; set; } = 30;
    public int DueRiskDays { get; set; } = 30;
    public string? AssigneeUserId { get; set; }
    public string? TeamId { get; set; }
    public List<string> Statuses { get; set; } = [];
}

public static class DashboardScopes
{
    public const string Personal = "Personal";
    public const string Project = "Project";
    public const string Portfolio = "Portfolio";
}

public static class DashboardWidgetTypes
{
    public const string ProjectSummary = "ProjectSummary";
    public const string StatusDistribution = "StatusDistribution";
    public const string UserWorkload = "UserWorkload";
    public const string DueDateRisks = "DueDateRisks";
    public const string FlowTime = "FlowTime";
    public const string CompletionRate = "CompletionRate";
    public const string TeamPerformance = "TeamPerformance";

    public static IReadOnlySet<string> Allowed { get; } = new HashSet<string>(
    [
        ProjectSummary,
        StatusDistribution,
        UserWorkload,
        DueDateRisks,
        FlowTime,
        CompletionRate,
        TeamPerformance
    ], StringComparer.Ordinal);
}
