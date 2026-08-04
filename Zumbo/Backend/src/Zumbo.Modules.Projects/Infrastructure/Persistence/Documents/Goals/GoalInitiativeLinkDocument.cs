using Zumbo.BuildingBlocks.Application.Persistence;

namespace Zumbo.Modules.Projects;

public sealed class GoalInitiativeLinkDocument
{
    public string PortfolioId { get; set; } = string.Empty;
    public string InitiativeId { get; set; } = string.Empty;
}
