using Zumbo.BuildingBlocks.Application.Persistence;

namespace Zumbo.Modules.WorkItems;

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
