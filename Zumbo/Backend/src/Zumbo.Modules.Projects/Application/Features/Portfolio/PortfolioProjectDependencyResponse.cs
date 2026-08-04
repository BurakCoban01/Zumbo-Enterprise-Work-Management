using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Projects;

public sealed record PortfolioProjectDependencyResponse(
    string Id,
    string SourceProjectId,
    string TargetProjectId,
    string Description,
    string Status,
    DateTimeOffset? RequiredBy);
