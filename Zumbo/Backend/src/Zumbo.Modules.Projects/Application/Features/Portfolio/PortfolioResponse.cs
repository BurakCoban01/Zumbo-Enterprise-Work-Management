using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Projects;

public sealed record PortfolioResponse(
    string Id,
    string OwnerUserId,
    string Name,
    string? Description,
    IReadOnlyCollection<string> ViewerUserIds,
    IReadOnlyCollection<InitiativeResponse> Initiatives,
    IReadOnlyCollection<PortfolioProjectDependencyResponse> Dependencies,
    bool CanEdit,
    bool Archived,
    DateTimeOffset UpdatedAt,
    long Version) : IVersionedResource;
