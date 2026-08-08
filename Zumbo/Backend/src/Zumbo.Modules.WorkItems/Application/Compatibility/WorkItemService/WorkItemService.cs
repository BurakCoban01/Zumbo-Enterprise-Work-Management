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
    private readonly SearchWorkItemsHandler searchWorkItemsHandler = new(
        workItems,
        currentUser,
        permissionChecker,
        typeSchemaPolicy ?? new LegacyWorkItemTypeSchemaPolicy(),
        searchIndex,
        activityStore,
        searchOptions ?? Options.Create(new SearchOptions()));
    private readonly SendDueDateRemindersHandler sendDueDateRemindersHandler = new(
        workItems,
        notifications,
        clock,
        permissionChecker,
        distributedLockProvider,
        distributedLockOptions,
        activityStore,
        expectedVersions);
    private readonly ProjectSummaryHandler projectSummaryHandler = new(
        workItems,
        clock,
        currentUser,
        permissionChecker,
        readModelCache,
        readModelCacheOptions);
    private readonly StatusDistributionHandler statusDistributionHandler = new(
        workItems,
        currentUser,
        permissionChecker,
        readModelCache,
        readModelCacheOptions);
    private readonly UserWorkloadHandler userWorkloadHandler = new(
        workItems,
        clock,
        currentUser,
        permissionChecker,
        readModelCache,
        readModelCacheOptions,
        activityStore);
    private readonly DueDateRisksHandler dueDateRisksHandler = new(
        workItems,
        clock,
        currentUser,
        permissionChecker,
        readModelCache,
        readModelCacheOptions);
    private readonly FlowTimeHandler flowTimeHandler = new(
        workItems,
        clock,
        currentUser,
        permissionChecker,
        readModelCache,
        readModelCacheOptions,
        activityStore);
    private readonly CompletionRateHandler completionRateHandler = new(
        workItems,
        clock,
        currentUser,
        permissionChecker,
        readModelCache,
        readModelCacheOptions);
    private readonly TeamPerformanceHandler teamPerformanceHandler = new(
        workItems,
        clock,
        currentUser,
        permissionChecker,
        teamPolicy,
        readModelCache,
        readModelCacheOptions,
        activityStore);
    private readonly CreateWorkItemHandler createWorkItemHandler = new(
        workItems,
        notifications,
        audit,
        clock,
        currentUser,
        permissionChecker,
        teamPolicy,
        boardPlacementPolicy,
        distributedLockProvider,
        distributedLockOptions,
        searchPublisher,
        realtimePublisher,
        cacheInvalidationPublisher,
        activityStore,
        graph,
        wipProjection,
        rankService ?? new(workItems, clock, Options.Create(new WorkItemRankOptions())),
        typeSchemaPolicy ?? new LegacyWorkItemTypeSchemaPolicy(),
        collaborationService,
        automationEvents,
        automationChain);
    private readonly GetWorkItemHandler getWorkItemHandler = new(
        workItems,
        currentUser,
        permissionChecker,
        activityStore);
    private readonly ArchiveWorkItemHandler archiveWorkItemHandler = new(
        workItems,
        audit,
        clock,
        currentUser,
        permissionChecker,
        distributedLockProvider,
        distributedLockOptions,
        searchPublisher,
        realtimePublisher,
        cacheInvalidationPublisher,
        activityStore,
        expectedVersions,
        wipProjection,
        collaborationService);
    private readonly RestoreWorkItemHandler restoreWorkItemHandler = new(
        workItems,
        audit,
        clock,
        currentUser,
        permissionChecker,
        boardPlacementPolicy,
        distributedLockProvider,
        distributedLockOptions,
        searchPublisher,
        realtimePublisher,
        cacheInvalidationPublisher,
        activityStore,
        expectedVersions,
        wipProjection,
        rankService ?? new(workItems, clock, Options.Create(new WorkItemRankOptions())),
        collaborationService);
    private readonly AddLabelHandler addLabelHandler = new(
        workItems,
        clock,
        currentUser,
        permissionChecker,
        searchPublisher,
        activityStore,
        expectedVersions,
        collaborationService,
        automationEvents,
        automationChain);
    private readonly RemoveLabelHandler removeLabelHandler = new(
        workItems,
        clock,
        currentUser,
        permissionChecker,
        searchPublisher,
        activityStore,
        expectedVersions,
        collaborationService,
        automationEvents,
        automationChain);
    private readonly AddChecklistItemHandler addChecklistItemHandler = new(
        workItems,
        clock,
        currentUser,
        permissionChecker,
        activityStore,
        expectedVersions,
        collaborationService);
    private readonly CompleteChecklistItemHandler completeChecklistItemHandler = new(
        workItems,
        clock,
        currentUser,
        permissionChecker,
        activityStore,
        expectedVersions,
        collaborationService);
    private readonly AddWorkLogHandler addWorkLogHandler = new(
        workItems,
        clock,
        currentUser,
        permissionChecker,
        activityStore,
        expectedVersions,
        cacheInvalidationPublisher,
        collaborationService);
    private readonly SetPlanningHandler setPlanningHandler = new(
        workItems,
        clock,
        currentUser,
        permissionChecker,
        sprintPolicy,
        searchPublisher,
        activityStore,
        expectedVersions,
        cacheInvalidationPublisher,
        collaborationService);
    private readonly MoveWorkItemHandler moveWorkItemHandler = new(
        workItems,
        audit,
        clock,
        currentUser,
        permissionChecker,
        workflowPolicy,
        boardPlacementPolicy,
        distributedLockProvider,
        distributedLockOptions,
        searchPublisher,
        realtimePublisher,
        cacheInvalidationPublisher,
        activityStore,
        graph,
        expectedVersions,
        wipProjection,
        rankService ?? new(workItems, clock, Options.Create(new WorkItemRankOptions())),
        collaborationService,
        automationEvents,
        automationChain);
    private readonly ReorderWorkItemHandler reorderWorkItemHandler = new(
        workItems,
        audit,
        clock,
        currentUser,
        permissionChecker,
        distributedLockProvider,
        distributedLockOptions,
        realtimePublisher,
        activityStore,
        expectedVersions,
        rankService ?? new(workItems, clock, Options.Create(new WorkItemRankOptions())),
        collaborationService);
    private readonly AddCommentHandler addCommentHandler = new(
        workItems,
        notifications,
        audit,
        clock,
        currentUser,
        permissionChecker,
        activityStore,
        expectedVersions,
        collaborationService,
        automationEvents,
        automationChain);
    private readonly EditCommentHandler editCommentHandler = new(
        workItems,
        audit,
        clock,
        currentUser,
        permissionChecker,
        activityStore,
        expectedVersions,
        collaborationService);
    private readonly DeleteCommentHandler deleteCommentHandler = new(
        workItems,
        audit,
        currentUser,
        permissionChecker,
        activityStore,
        expectedVersions,
        collaborationService);
    private readonly SetParentHandler setParentHandler = new(
        workItems,
        audit,
        clock,
        currentUser,
        permissionChecker,
        typeSchemaPolicy ?? new LegacyWorkItemTypeSchemaPolicy(),
        distributedLockProvider,
        distributedLockOptions,
        activityStore,
        graph,
        expectedVersions,
        collaborationService);
    private readonly LinkWorkItemHandler linkWorkItemHandler = new(
        workItems,
        audit,
        clock,
        currentUser,
        permissionChecker,
        distributedLockProvider,
        distributedLockOptions,
        activityStore,
        graph,
        expectedVersions,
        collaborationService);
    private readonly UnlinkWorkItemHandler unlinkWorkItemHandler = new(
        workItems,
        audit,
        clock,
        currentUser,
        permissionChecker,
        distributedLockProvider,
        distributedLockOptions,
        activityStore,
        graph,
        expectedVersions,
        collaborationService);
    private readonly OpenAttachmentHandler openAttachmentHandler = new(
        workItems,
        currentUser,
        permissionChecker,
        activityStore,
        attachmentStorage);
    private readonly UploadAttachmentHandler uploadAttachmentHandler = new(
        workItems,
        audit,
        clock,
        currentUser,
        permissionChecker,
        attachmentStorage,
        distributedLockProvider,
        distributedLockOptions,
        activityStore,
        expectedVersions,
        collaborationService,
        logger);
    private readonly DeleteAttachmentHandler deleteAttachmentHandler = new(
        workItems,
        audit,
        currentUser,
        permissionChecker,
        attachmentStorage,
        distributedLockProvider,
        distributedLockOptions,
        activityStore,
        expectedVersions,
        collaborationService,
        logger);
    private readonly ClearAssigneeHandler clearAssigneeHandler = new(
        workItems,
        audit,
        clock,
        currentUser,
        permissionChecker,
        searchPublisher,
        realtimePublisher,
        cacheInvalidationPublisher,
        activityStore,
        expectedVersions,
        collaborationService,
        automationEvents,
        automationChain);
    private readonly AssignWorkItemHandler assignWorkItemHandler = new(
        workItems,
        audit,
        clock,
        currentUser,
        permissionChecker,
        searchPublisher,
        realtimePublisher,
        cacheInvalidationPublisher,
        activityStore,
        expectedVersions,
        collaborationService,
        teamPolicy,
        notifications);
    private readonly SetWorkItemTeamHandler setWorkItemTeamHandler = new(
        workItems,
        audit,
        clock,
        currentUser,
        permissionChecker,
        searchPublisher,
        realtimePublisher,
        cacheInvalidationPublisher,
        activityStore,
        expectedVersions,
        collaborationService,
        teamPolicy);
    private readonly SetCustomFieldsHandler setCustomFieldsHandler = new(
        workItems,
        audit,
        clock,
        currentUser,
        permissionChecker,
        typeSchemaPolicy ?? new LegacyWorkItemTypeSchemaPolicy(),
        searchPublisher,
        realtimePublisher,
        cacheInvalidationPublisher,
        activityStore,
        expectedVersions,
        collaborationService,
        automationEvents,
        automationChain);
    private readonly RequestApprovalHandler requestApprovalHandler = new(
        workItems,
        notifications,
        audit,
        clock,
        currentUser,
        permissionChecker,
        workflowPolicy,
        activityStore,
        expectedVersions,
        collaborationService);
    private readonly UpdateWorkItemHandler updateWorkItemHandler = new(
        workItems,
        audit,
        clock,
        currentUser,
        permissionChecker,
        searchPublisher,
        realtimePublisher,
        cacheInvalidationPublisher,
        activityStore,
        expectedVersions,
        collaborationService,
        automationEvents,
        automationChain);
    private readonly DecideApprovalHandler decideApprovalHandler = new(
        workItems,
        notifications,
        audit,
        clock,
        currentUser,
        permissionChecker,
        activityStore,
        expectedVersions,
        collaborationService);
}
