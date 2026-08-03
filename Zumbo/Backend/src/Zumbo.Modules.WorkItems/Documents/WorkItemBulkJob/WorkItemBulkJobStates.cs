using Zumbo.BuildingBlocks.Application.Persistence;

namespace Zumbo.Modules.WorkItems;

public static class WorkItemBulkJobStates
{
    public const string Pending = "Pending";
    public const string Running = "Running";
    public const string Completed = "Completed";
    public const string CompletedWithErrors = "CompletedWithErrors";
    public const string Cancelled = "Cancelled";
    public const string Failed = "Failed";

    public static bool IsTerminal(string state) => state is Completed or CompletedWithErrors or Cancelled;
}
