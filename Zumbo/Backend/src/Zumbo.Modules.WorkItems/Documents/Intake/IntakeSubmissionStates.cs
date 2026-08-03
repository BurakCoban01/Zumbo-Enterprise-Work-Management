using Zumbo.BuildingBlocks.Application.Persistence;

namespace Zumbo.Modules.WorkItems;

public static class IntakeSubmissionStates
{
    public const string Processing = "Processing";
    public const string New = "New";
    public const string InReview = "InReview";
    public const string Resolved = "Resolved";
    public const string Rejected = "Rejected";
}
