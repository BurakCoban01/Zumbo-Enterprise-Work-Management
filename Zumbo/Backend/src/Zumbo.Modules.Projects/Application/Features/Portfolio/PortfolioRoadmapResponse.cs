using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Projects;

public sealed record PortfolioRoadmapResponse(
    string PortfolioId,
    string SourceStatus,
    DateTimeOffset GeneratedAt,
    IReadOnlyCollection<string> UnavailableProjectIds,
    IReadOnlyCollection<PortfolioRoadmapInitiativeResponse> Initiatives,
    IReadOnlyCollection<PortfolioProjectDependencyResponse> Dependencies);
