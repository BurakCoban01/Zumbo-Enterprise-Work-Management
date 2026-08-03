using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Projects;

public sealed partial class PortfolioService(
    IDocumentRepository<PortfolioDocument> portfolios,
    IPortfolioDirectory directory,
    IPortfolioAuditWriter audit,
    ICurrentUser currentUser,
    IClock clock,
    IExpectedVersionAccessor? expectedVersions = null)
{
    private const int MaximumInitiatives = 100;
    private const int MaximumProjectsPerInitiative = 20;
    private const int MaximumDependencies = 200;
    private const int MaximumHierarchyDepth = 5;
    private readonly ExpectedVersionState expectedVersion = new(expectedVersions);
}
