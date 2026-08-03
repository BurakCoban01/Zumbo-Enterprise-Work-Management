using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Linq.Expressions;
using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Runtime;
using Zumbo.BuildingBlocks.Application.Search;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

public sealed partial class WorkItemService(
    IDocumentRepository<WorkItemDocument> workItems,
    IWorkItemNotificationPublisher notifications,
    IWorkItemAuditPublisher audit,
    IClock clock,
    ICurrentUser currentUser,
    IProjectPermissionChecker permissionChecker,
    IWorkItemTeamPolicy teamPolicy,
    IWorkflowPolicy workflowPolicy,
    IBoardPlacementPolicy boardPlacementPolicy,
    IAttachmentStorage attachmentStorage,
    IDistributedLockProvider distributedLockProvider,
    IOptions<DistributedLockOptions> distributedLockOptions,
    IWorkItemSearchIndex searchIndex,
    IWorkItemSearchPublisher searchPublisher,
    IWorkItemRealtimePublisher realtimePublisher,
    IWorkItemReadModelCache readModelCache,
    IWorkItemCacheInvalidationPublisher cacheInvalidationPublisher,
    IOptions<WorkItemReadModelCacheOptions> readModelCacheOptions,
    IWorkItemActivityStore activityStore,
    WorkItemGraphService graph,
    IExpectedVersionAccessor? expectedVersions = null,
    WorkItemWipProjection? wipProjection = null,
    WorkItemRankService? rankService = null,
    IWorkItemSprintPolicy? sprintPolicy = null,
    IWorkItemTypeSchemaPolicy? typeSchemaPolicy = null,
    WorkItemCollaborationService? collaborationService = null,
    IOptions<SearchOptions>? searchOptions = null,
    IWorkItemAutomationEventPublisher? automationEvents = null,
    IWorkItemAutomationChainContextAccessor? automationChain = null,
    ILogger<WorkItemService>? logger = null) : IIntakeWorkItemCreator
{
    private readonly ILogger<WorkItemService>? compensationLogger = logger;
    private readonly ExpectedVersionState expectedVersion = new(expectedVersions);
    private readonly Dictionary<string, string> authorizedOrganizationIds = new(StringComparer.Ordinal);
    private readonly WorkItemRankService ranks = rankService ?? new(workItems, clock, Options.Create(new WorkItemRankOptions()));
    private readonly IWorkItemTypeSchemaPolicy typeSchemas = typeSchemaPolicy ?? new LegacyWorkItemTypeSchemaPolicy();
    private readonly SearchOptions searchRuntimeOptions = searchOptions?.Value ?? new SearchOptions();
}
