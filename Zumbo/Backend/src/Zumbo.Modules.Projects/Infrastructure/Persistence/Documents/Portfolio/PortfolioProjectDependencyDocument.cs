using Zumbo.BuildingBlocks.Application.Persistence;

namespace Zumbo.Modules.Projects;

public sealed class PortfolioProjectDependencyDocument
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string SourceProjectId { get; set; } = string.Empty;
    public string TargetProjectId { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Status { get; set; } = PortfolioDependencyStatuses.Active;
    public DateTimeOffset? RequiredBy { get; set; }
}
