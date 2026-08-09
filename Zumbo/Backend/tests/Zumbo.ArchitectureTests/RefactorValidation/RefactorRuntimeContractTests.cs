using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Zumbo.ArchitectureTests.RefactorValidation;

public sealed class RefactorRuntimeContractTests
{
    private static readonly HashSet<string> HttpMapMethods =
        ["MapGet", "MapPost", "MapPut", "MapPatch", "MapDelete", "MapMethods"];

    private static readonly HashSet<string> DiMethods =
    [
        "AddScoped", "AddSingleton", "AddTransient",
        "TryAddScoped", "TryAddSingleton", "TryAddTransient",
        "AddKeyedScoped", "AddKeyedSingleton", "AddKeyedTransient",
        "AddHostedService", "Configure", "PostConfigure", "AddOptions", "AddHttpClient"
    ];

    private static readonly string[] ReplacedVerticalSliceDiRegistrations =
    [
        "services.AddScoped<IWorkItemTypeSchemaPolicy>(provider=>provider.GetRequiredService<WorkItemTypeSchemaService>());",
        "services.AddScoped<RegisterUserHandler>();",
        "services.AddScoped<SearchUsersHandler>();",
        "services.AddScoped<CreateOrganizationHandler>();",
        "services.AddScoped<ListOrganizationsHandler>();",
        "services.AddScoped<CreateTeamHandler>();",
        "services.AddScoped<ListTeamsHandler>();",
        "services.AddScoped<CreateProjectHandler>();",
        "services.AddScoped<ListProjectsHandler>();",
        "services.AddScoped<CreateBoardHandler>();",
        "services.AddScoped<ListBoardsByProjectHandler>();",
        "services.AddScoped<UpsertWorkflowHandler>();",
        "services.AddScoped<GetWorkflowHandler>();",
        "services.AddScoped<CreateWorkItemHandler>();",
        "services.AddScoped<IIntakeWorkItemCreator>(provider=>provider.GetRequiredService<WorkItemService>());",
        "services.AddScoped<SearchWorkItemsHandler>();",
        "services.AddScoped<WorkItemBulkJobProcessor>();",
        "services.AddScoped<IAutomationActionExecutor,AutomationWorkItemActionExecutor>();",
        "services.AddScoped<DashboardRenderer>();",
        "services.AddScoped<ListNotificationsHandler>();",
        "services.AddScoped<MarkNotificationAsReadHandler>();",
        "services.AddScoped<WriteAuditLogHandler>();",
        "services.AddScoped<QueryAuditLogHandler>();",
        "services.AddScoped<IWorkItemWebhookDelivery,WorkItemWebhookDeliveryAdapter>();",
        "services.AddScoped<IDurableEventHandler,DevelopmentWebhookDurableHandler>();"
    ];

    private static readonly string[] PortFocusedVerticalSliceDiRegistrations =
    [
        "services.AddScoped<ListPortfoliosHandler>(provider=>newListPortfoliosHandler("
        + "provider.GetRequiredService<IDocumentRepository<PortfolioDocument>>(),"
        + "provider.GetRequiredService<ICurrentUser>()));",
        "services.AddScoped<GetPortfolioHandler>(provider=>newGetPortfolioHandler("
        + "provider.GetRequiredService<IDocumentRepository<PortfolioDocument>>(),"
        + "provider.GetRequiredService<ICurrentUser>()));",
        "services.AddScoped<GetPortfolioRoadmapHandler>(provider=>newGetPortfolioRoadmapHandler("
        + "provider.GetRequiredService<IDocumentRepository<PortfolioDocument>>(),"
        + "provider.GetRequiredService<IPortfolioDirectory>(),"
        + "provider.GetRequiredService<ICurrentUser>(),"
        + "provider.GetRequiredService<IClock>()));",
        "services.AddScoped<SavePortfolioHandler>(provider=>newSavePortfolioHandler("
        + "provider.GetRequiredService<IDocumentRepository<PortfolioDocument>>(),"
        + "provider.GetRequiredService<IPortfolioDirectory>(),"
        + "provider.GetRequiredService<IPortfolioAuditWriter>(),"
        + "provider.GetRequiredService<ICurrentUser>(),"
        + "provider.GetRequiredService<IClock>(),"
        + "provider.GetService<IExpectedVersionAccessor>()));",
        "services.AddScoped<ArchivePortfolioHandler>(provider=>newArchivePortfolioHandler("
        + "provider.GetRequiredService<IDocumentRepository<PortfolioDocument>>(),"
        + "provider.GetRequiredService<IPortfolioAuditWriter>(),"
        + "provider.GetRequiredService<ICurrentUser>(),"
        + "provider.GetRequiredService<IClock>(),"
        + "provider.GetService<IExpectedVersionAccessor>()));",
        "services.AddScoped<SaveInitiativeHandler>(provider=>newSaveInitiativeHandler("
        + "provider.GetRequiredService<IDocumentRepository<PortfolioDocument>>(),"
        + "provider.GetRequiredService<IPortfolioDirectory>(),"
        + "provider.GetRequiredService<IPortfolioAuditWriter>(),"
        + "provider.GetRequiredService<ICurrentUser>(),"
        + "provider.GetRequiredService<IClock>(),"
        + "provider.GetService<IExpectedVersionAccessor>()));",
        "services.AddScoped<AddInitiativeStatusUpdateHandler>(provider=>newAddInitiativeStatusUpdateHandler("
        + "provider.GetRequiredService<IDocumentRepository<PortfolioDocument>>(),"
        + "provider.GetRequiredService<IPortfolioAuditWriter>(),"
        + "provider.GetRequiredService<ICurrentUser>(),"
        + "provider.GetRequiredService<IClock>(),"
        + "provider.GetService<IExpectedVersionAccessor>()));",
        "services.AddScoped<SavePortfolioDependencyHandler>(provider=>newSavePortfolioDependencyHandler("
        + "provider.GetRequiredService<IDocumentRepository<PortfolioDocument>>(),"
        + "provider.GetRequiredService<IPortfolioDirectory>(),"
        + "provider.GetRequiredService<IPortfolioAuditWriter>(),"
        + "provider.GetRequiredService<ICurrentUser>(),"
        + "provider.GetRequiredService<IClock>(),"
        + "provider.GetService<IExpectedVersionAccessor>()));",
        "services.AddScoped<ArchiveGoalHandler>(provider=>newArchiveGoalHandler("
        + "provider.GetRequiredService<IDocumentRepository<GoalDocument>>(),"
        + "provider.GetRequiredService<IGoalAuditWriter>(),"
        + "provider.GetRequiredService<ICurrentUser>(),"
        + "provider.GetRequiredService<IClock>(),"
        + "provider.GetService<IExpectedVersionAccessor>()));",
        "services.AddScoped<SaveGoalHandler>(provider=>newSaveGoalHandler("
        + "provider.GetRequiredService<IDocumentRepository<GoalDocument>>(),"
        + "provider.GetRequiredService<IGoalDirectory>(),"
        + "provider.GetRequiredService<IGoalAuditWriter>(),"
        + "provider.GetRequiredService<ICurrentUser>(),"
        + "provider.GetRequiredService<IClock>(),"
        + "provider.GetService<IExpectedVersionAccessor>()));",
        "services.AddScoped<SaveKeyResultHandler>(provider=>newSaveKeyResultHandler("
        + "provider.GetRequiredService<IDocumentRepository<GoalDocument>>(),"
        + "provider.GetRequiredService<IGoalDirectory>(),"
        + "provider.GetRequiredService<IGoalAuditWriter>(),"
        + "provider.GetRequiredService<ICurrentUser>(),"
        + "provider.GetRequiredService<IClock>(),"
        + "provider.GetService<IExpectedVersionAccessor>()));",
        "services.AddScoped<AddGoalStatusUpdateHandler>(provider=>newAddGoalStatusUpdateHandler("
        + "provider.GetRequiredService<IDocumentRepository<GoalDocument>>(),"
        + "provider.GetRequiredService<IGoalAuditWriter>(),"
        + "provider.GetRequiredService<ICurrentUser>(),"
        + "provider.GetRequiredService<IClock>(),"
        + "provider.GetService<IExpectedVersionAccessor>()));",
        "services.AddScoped<AddKeyResultProgressHandler>(provider=>newAddKeyResultProgressHandler("
        + "provider.GetRequiredService<IDocumentRepository<GoalDocument>>(),"
        + "provider.GetRequiredService<IGoalAuditWriter>(),"
        + "provider.GetRequiredService<ICurrentUser>(),"
        + "provider.GetRequiredService<IClock>(),"
        + "provider.GetService<IExpectedVersionAccessor>()));",
        "services.AddScoped<CompleteSprintHandler>(provider=>newCompleteSprintHandler("
        + "provider.GetRequiredService<IDocumentRepository<SprintDocument>>(),"
        + "provider.GetRequiredService<IDocumentRepository<SprintScopeSnapshotDocument>>(),"
        + "provider.GetRequiredService<IDocumentRepository<SprintCompletionSnapshotDocument>>(),"
        + "provider.GetRequiredService<IDocumentRepository<WorkItemDocument>>(),"
        + "provider.GetRequiredService<IProjectPermissionChecker>(),"
        + "provider.GetRequiredService<IWorkItemAuditPublisher>(),"
        + "provider.GetRequiredService<IDistributedLockProvider>(),"
        + "provider.GetRequiredService<IOptions<DistributedLockOptions>>(),"
        + "provider.GetRequiredService<IOptions<SprintOptions>>(),"
        + "provider.GetRequiredService<IClock>(),"
        + "provider.GetRequiredService<ICurrentUser>(),"
        + "provider.GetRequiredService<IWorkItemCacheInvalidationPublisher>(),"
        + "provider.GetService<IExpectedVersionAccessor>()));",
        "services.AddScoped<CreateSprintHandler>(provider=>newCreateSprintHandler("
        + "provider.GetRequiredService<IDocumentRepository<SprintDocument>>(),"
        + "provider.GetRequiredService<IProjectPermissionChecker>(),"
        + "provider.GetRequiredService<IWorkItemAuditPublisher>(),"
        + "provider.GetRequiredService<IDistributedLockProvider>(),"
        + "provider.GetRequiredService<IOptions<DistributedLockOptions>>(),"
        + "provider.GetRequiredService<IClock>(),"
        + "provider.GetRequiredService<ICurrentUser>(),"
        + "provider.GetRequiredService<IWorkItemCacheInvalidationPublisher>()));",
        "services.AddScoped<GetCustomFieldDistributionHandler>(provider=>newGetCustomFieldDistributionHandler("
        + "provider.GetRequiredService<IDocumentRepository<WorkItemTypeSchemaDocument>>(),"
        + "provider.GetRequiredService<IDocumentRepository<WorkItemDocument>>(),"
        + "provider.GetRequiredService<IProjectPermissionChecker>(),"
        + "provider.GetRequiredService<ICurrentUser>(),"
        + "provider.GetRequiredService<IOptions<WorkItemTypeSchemaOptions>>(),"
        + "provider.GetRequiredService<IClock>()));",
        "services.AddScoped<GetGoalHandler>(provider=>newGetGoalHandler("
        + "provider.GetRequiredService<IDocumentRepository<GoalDocument>>(),"
        + "provider.GetRequiredService<ICurrentUser>()));",
        "services.AddScoped<GetGoalRollupHandler>(provider=>newGetGoalRollupHandler("
        + "provider.GetRequiredService<IDocumentRepository<GoalDocument>>(),"
        + "provider.GetRequiredService<IGoalDirectory>(),"
        + "provider.GetRequiredService<ICurrentUser>(),"
        + "provider.GetRequiredService<IClock>()));",
        "services.AddScoped<GetIssueTypeDistributionHandler>(provider=>newGetIssueTypeDistributionHandler("
        + "provider.GetRequiredService<IDocumentRepository<WorkItemTypeSchemaDocument>>(),"
        + "provider.GetRequiredService<IDocumentRepository<WorkItemDocument>>(),"
        + "provider.GetRequiredService<IProjectPermissionChecker>(),"
        + "provider.GetRequiredService<ICurrentUser>(),"
        + "provider.GetRequiredService<IOptions<WorkItemTypeSchemaOptions>>(),"
        + "provider.GetRequiredService<IClock>()));",
        "services.AddScoped<GetIssueTypeHierarchyHandler>(provider=>newGetIssueTypeHierarchyHandler("
        + "provider.GetRequiredService<IDocumentRepository<WorkItemTypeSchemaDocument>>(),"
        + "provider.GetRequiredService<IClock>()));",
        "services.AddScoped<GetSprintBurndownHandler>(provider=>newGetSprintBurndownHandler("
        + "provider.GetRequiredService<IDocumentRepository<SprintDocument>>(),"
        + "provider.GetRequiredService<IDocumentRepository<SprintScopeSnapshotDocument>>(),"
        + "provider.GetRequiredService<IDocumentRepository<SprintCompletionSnapshotDocument>>(),"
        + "provider.GetRequiredService<IDocumentRepository<WorkItemDocument>>(),"
        + "provider.GetRequiredService<IProjectPermissionChecker>(),"
        + "provider.GetRequiredService<ICurrentUser>(),"
        + "provider.GetRequiredService<IOptions<SprintOptions>>(),"
        + "provider.GetRequiredService<IWorkItemReadModelCache>(),"
        + "provider.GetRequiredService<IOptions<WorkItemReadModelCacheOptions>>()));",
        "services.AddScoped<GetSprintHandler>(provider=>newGetSprintHandler("
        + "provider.GetRequiredService<IDocumentRepository<SprintDocument>>(),"
        + "provider.GetRequiredService<IDocumentRepository<WorkItemDocument>>(),"
        + "provider.GetRequiredService<IProjectPermissionChecker>(),"
        + "provider.GetRequiredService<ICurrentUser>()));",
        "services.AddScoped<GetSprintVelocityHandler>(provider=>newGetSprintVelocityHandler("
        + "provider.GetRequiredService<IDocumentRepository<SprintDocument>>(),"
        + "provider.GetRequiredService<IProjectPermissionChecker>(),"
        + "provider.GetRequiredService<ICurrentUser>(),"
        + "provider.GetRequiredService<IWorkItemReadModelCache>(),"
        + "provider.GetRequiredService<IOptions<WorkItemReadModelCacheOptions>>()));",
        "services.AddScoped<GetWorkItemTypeSchemaHandler>(provider=>newGetWorkItemTypeSchemaHandler("
        + "provider.GetRequiredService<IDocumentRepository<WorkItemTypeSchemaDocument>>(),"
        + "provider.GetRequiredService<IDocumentRepository<WorkItemDocument>>(),"
        + "provider.GetRequiredService<IProjectPermissionChecker>(),"
        + "provider.GetRequiredService<ICurrentUser>(),"
        + "provider.GetRequiredService<IOptions<WorkItemTypeSchemaOptions>>(),"
        + "provider.GetRequiredService<IClock>()));",
        "services.AddScoped<IWorkItemTypeSchemaPolicy,WorkItemTypeSchemaPolicyAdapter>();",
        "services.AddScoped<ListSprintBacklogHandler>(provider=>newListSprintBacklogHandler("
        + "provider.GetRequiredService<IDocumentRepository<SprintDocument>>(),"
        + "provider.GetRequiredService<IDocumentRepository<WorkItemDocument>>(),"
        + "provider.GetRequiredService<IProjectPermissionChecker>(),"
        + "provider.GetRequiredService<ICurrentUser>()));",
        "services.AddScoped<ListGoalsHandler>(provider=>newListGoalsHandler("
        + "provider.GetRequiredService<IDocumentRepository<GoalDocument>>(),"
        + "provider.GetRequiredService<ICurrentUser>()));",
        "services.AddScoped<ListSprintsHandler>(provider=>newListSprintsHandler("
        + "provider.GetRequiredService<IDocumentRepository<SprintDocument>>(),"
        + "provider.GetRequiredService<IDocumentRepository<WorkItemDocument>>(),"
        + "provider.GetRequiredService<IProjectPermissionChecker>(),"
        + "provider.GetRequiredService<ICurrentUser>()));",
        "services.AddScoped<PlanSprintWorkItemHandler>(provider=>newPlanSprintWorkItemHandler("
        + "provider.GetRequiredService<IDocumentRepository<SprintDocument>>(),"
        + "provider.GetRequiredService<IDocumentRepository<WorkItemDocument>>(),"
        + "provider.GetRequiredService<IProjectPermissionChecker>(),"
        + "provider.GetRequiredService<IWorkItemAuditPublisher>(),"
        + "provider.GetRequiredService<IDistributedLockProvider>(),"
        + "provider.GetRequiredService<IOptions<DistributedLockOptions>>(),"
        + "provider.GetRequiredService<IClock>(),"
        + "provider.GetRequiredService<ICurrentUser>(),"
        + "provider.GetRequiredService<IWorkItemCacheInvalidationPublisher>(),"
        + "provider.GetService<IExpectedVersionAccessor>()));",
        "services.AddScoped<StartSprintHandler>(provider=>newStartSprintHandler("
        + "provider.GetRequiredService<IDocumentRepository<SprintDocument>>(),"
        + "provider.GetRequiredService<IDocumentRepository<SprintScopeSnapshotDocument>>(),"
        + "provider.GetRequiredService<IDocumentRepository<WorkItemDocument>>(),"
        + "provider.GetRequiredService<IProjectPermissionChecker>(),"
        + "provider.GetRequiredService<IWorkItemAuditPublisher>(),"
        + "provider.GetRequiredService<IDistributedLockProvider>(),"
        + "provider.GetRequiredService<IOptions<DistributedLockOptions>>(),"
        + "provider.GetRequiredService<IOptions<SprintOptions>>(),"
        + "provider.GetRequiredService<IClock>(),"
        + "provider.GetRequiredService<ICurrentUser>(),"
        + "provider.GetRequiredService<IWorkItemCacheInvalidationPublisher>(),"
        + "provider.GetService<IExpectedVersionAccessor>()));",
        "services.AddScoped<UpsertWorkItemTypeSchemaHandler>(provider=>newUpsertWorkItemTypeSchemaHandler("
        + "provider.GetRequiredService<IDocumentRepository<WorkItemTypeSchemaDocument>>(),"
        + "provider.GetRequiredService<IDocumentRepository<WorkItemDocument>>(),"
        + "provider.GetRequiredService<IProjectPermissionChecker>(),"
        + "provider.GetRequiredService<IWorkItemAuditPublisher>(),"
        + "provider.GetRequiredService<IDistributedLockProvider>(),"
        + "provider.GetRequiredService<IOptions<DistributedLockOptions>>(),"
        + "provider.GetRequiredService<IOptions<WorkItemTypeSchemaOptions>>(),"
        + "provider.GetRequiredService<IClock>(),"
        + "provider.GetRequiredService<ICurrentUser>(),"
        + "provider.GetService<IExpectedVersionAccessor>()));",
        "services.AddScoped<UnplanSprintWorkItemHandler>(provider=>newUnplanSprintWorkItemHandler("
        + "provider.GetRequiredService<IDocumentRepository<SprintDocument>>(),"
        + "provider.GetRequiredService<IDocumentRepository<WorkItemDocument>>(),"
        + "provider.GetRequiredService<IProjectPermissionChecker>(),"
        + "provider.GetRequiredService<IWorkItemAuditPublisher>(),"
        + "provider.GetRequiredService<IDistributedLockProvider>(),"
        + "provider.GetRequiredService<IOptions<DistributedLockOptions>>(),"
        + "provider.GetRequiredService<IClock>(),"
        + "provider.GetRequiredService<ICurrentUser>(),"
        + "provider.GetRequiredService<IWorkItemCacheInvalidationPublisher>(),"
        + "provider.GetService<IExpectedVersionAccessor>()));",
        "services.AddScoped<ValidateWorkItemSearchFilterHandler>(provider=>newValidateWorkItemSearchFilterHandler("
        + "provider.GetRequiredService<IDocumentRepository<WorkItemTypeSchemaDocument>>(),"
        + "provider.GetRequiredService<IClock>()));",
        "services.AddScoped<ValidateWorkItemShapeHandler>(provider=>newValidateWorkItemShapeHandler("
        + "provider.GetRequiredService<IDocumentRepository<WorkItemTypeSchemaDocument>>(),"
        + "provider.GetRequiredService<IClock>()));",
        "services.AddScoped<ListWorkItemTemplatesHandler>(provider=>newListWorkItemTemplatesHandler("
        + "provider.GetRequiredService<IDocumentRepository<WorkItemTemplateDocument>>(),"
        + "provider.GetRequiredService<IProjectPermissionChecker>(),"
        + "provider.GetRequiredService<ICurrentUser>()));",
        "services.AddScoped<ListWorkItemRecurrencesHandler>(provider=>newListWorkItemRecurrencesHandler("
        + "provider.GetRequiredService<IDocumentRepository<WorkItemRecurrenceDocument>>(),"
        + "provider.GetRequiredService<IDocumentRepository<WorkItemRecurrenceOccurrenceDocument>>(),"
        + "provider.GetRequiredService<IProjectPermissionChecker>(),"
        + "provider.GetRequiredService<ICurrentUser>()));",
        "services.AddScoped<ListRecurrenceOccurrencesHandler>(provider=>newListRecurrenceOccurrencesHandler("
        + "provider.GetRequiredService<IDocumentRepository<WorkItemRecurrenceDocument>>(),"
        + "provider.GetRequiredService<IDocumentRepository<WorkItemRecurrenceOccurrenceDocument>>(),"
        + "provider.GetRequiredService<IProjectPermissionChecker>(),"
        + "provider.GetRequiredService<ICurrentUser>()));",
        "services.AddScoped<PreviewWorkItemRecurrenceHandler>(provider=>newPreviewWorkItemRecurrenceHandler("
        + "provider.GetRequiredService<IDocumentRepository<WorkItemTemplateDocument>>(),"
        + "provider.GetRequiredService<IProjectPermissionChecker>(),"
        + "provider.GetRequiredService<ICurrentUser>(),"
        + "provider.GetRequiredService<IOptions<WorkItemRecurrenceOptions>>(),"
        + "provider.GetRequiredService<IClock>()));",
        "services.AddScoped<CreateWorkItemRecurrenceHandler>(provider=>newCreateWorkItemRecurrenceHandler("
        + "provider.GetRequiredService<IDocumentRepository<WorkItemTemplateDocument>>(),"
        + "provider.GetRequiredService<IDocumentRepository<WorkItemRecurrenceDocument>>(),"
        + "provider.GetRequiredService<IDocumentRepository<WorkItemRecurrenceOccurrenceDocument>>(),"
        + "provider.GetRequiredService<IProjectPermissionChecker>(),"
        + "provider.GetRequiredService<ICurrentUser>(),"
        + "provider.GetRequiredService<IOptions<WorkItemRecurrenceOptions>>(),"
        + "provider.GetRequiredService<IClock>(),"
        + "provider.GetRequiredService<IWorkItemAuditPublisher>()));",
        "services.AddScoped<SetWorkItemRecurrenceStateHandler>(provider=>newSetWorkItemRecurrenceStateHandler("
        + "provider.GetRequiredService<IDocumentRepository<WorkItemRecurrenceDocument>>(),"
        + "provider.GetRequiredService<IDocumentRepository<WorkItemRecurrenceOccurrenceDocument>>(),"
        + "provider.GetRequiredService<IProjectPermissionChecker>(),"
        + "provider.GetRequiredService<ICurrentUser>(),"
        + "provider.GetRequiredService<IDistributedLockProvider>(),"
        + "provider.GetRequiredService<IOptions<DistributedLockOptions>>(),"
        + "provider.GetRequiredService<IClock>(),"
        + "provider.GetRequiredService<IWorkItemAuditPublisher>(),"
        + "provider.GetService<IExpectedVersionAccessor>()));",
        "services.AddScoped<ArchiveWorkItemRecurrenceHandler>(provider=>newArchiveWorkItemRecurrenceHandler("
        + "provider.GetRequiredService<IDocumentRepository<WorkItemRecurrenceDocument>>(),"
        + "provider.GetRequiredService<IProjectPermissionChecker>(),"
        + "provider.GetRequiredService<ICurrentUser>(),"
        + "provider.GetRequiredService<IDistributedLockProvider>(),"
        + "provider.GetRequiredService<IOptions<DistributedLockOptions>>(),"
        + "provider.GetRequiredService<IClock>(),"
        + "provider.GetRequiredService<IWorkItemAuditPublisher>(),"
        + "provider.GetService<IExpectedVersionAccessor>()));",
        "services.AddScoped<CreateWorkItemTemplateHandler>(provider=>newCreateWorkItemTemplateHandler("
        + "provider.GetRequiredService<IDocumentRepository<WorkItemTemplateDocument>>(),"
        + "provider.GetRequiredService<IProjectPermissionChecker>(),"
        + "provider.GetRequiredService<ICurrentUser>(),"
        + "provider.GetRequiredService<IWorkItemTeamPolicy>(),"
        + "provider.GetRequiredService<IWorkItemCollaboratorDirectory>(),"
        + "provider.GetRequiredService<IBoardPlacementPolicy>(),"
        + "provider.GetRequiredService<IWorkItemTypeSchemaPolicy>(),"
        + "provider.GetRequiredService<IDistributedLockProvider>(),"
        + "provider.GetRequiredService<IOptions<DistributedLockOptions>>(),"
        + "provider.GetRequiredService<IClock>(),"
        + "provider.GetRequiredService<IWorkItemAuditPublisher>()));",
        "services.AddScoped<UpdateWorkItemTemplateHandler>(provider=>newUpdateWorkItemTemplateHandler("
        + "provider.GetRequiredService<IDocumentRepository<WorkItemTemplateDocument>>(),"
        + "provider.GetRequiredService<IDocumentRepository<WorkItemRecurrenceDocument>>(),"
        + "provider.GetRequiredService<IProjectPermissionChecker>(),"
        + "provider.GetRequiredService<ICurrentUser>(),"
        + "provider.GetRequiredService<IWorkItemTeamPolicy>(),"
        + "provider.GetRequiredService<IWorkItemCollaboratorDirectory>(),"
        + "provider.GetRequiredService<IBoardPlacementPolicy>(),"
        + "provider.GetRequiredService<IWorkItemTypeSchemaPolicy>(),"
        + "provider.GetRequiredService<IDistributedLockProvider>(),"
        + "provider.GetRequiredService<IOptions<DistributedLockOptions>>(),"
        + "provider.GetRequiredService<IClock>(),"
        + "provider.GetRequiredService<IWorkItemAuditPublisher>(),"
        + "provider.GetService<IExpectedVersionAccessor>()));",
        "services.AddScoped<ArchiveWorkItemTemplateHandler>(provider=>newArchiveWorkItemTemplateHandler("
        + "provider.GetRequiredService<IDocumentRepository<WorkItemTemplateDocument>>(),"
        + "provider.GetRequiredService<IDocumentRepository<WorkItemRecurrenceDocument>>(),"
        + "provider.GetRequiredService<IProjectPermissionChecker>(),"
        + "provider.GetRequiredService<ICurrentUser>(),"
        + "provider.GetRequiredService<IDistributedLockProvider>(),"
        + "provider.GetRequiredService<IOptions<DistributedLockOptions>>(),"
        + "provider.GetRequiredService<IClock>(),"
        + "provider.GetRequiredService<IWorkItemAuditPublisher>(),"
        + "provider.GetService<IExpectedVersionAccessor>()));",
        "services.AddScoped<ScheduleDueRecurrencesHandler>(provider=>newScheduleDueRecurrencesHandler("
        + "provider.GetRequiredService<IDocumentRepository<WorkItemTemplateDocument>>(),"
        + "provider.GetRequiredService<IDocumentRepository<WorkItemRecurrenceDocument>>(),"
        + "provider.GetRequiredService<IDocumentRepository<WorkItemRecurrenceOccurrenceDocument>>(),"
        + "provider.GetRequiredService<IWorkItemRecurrenceEventPublisher>(),"
        + "provider.GetRequiredService<IDistributedLockProvider>(),"
        + "provider.GetRequiredService<IOptions<DistributedLockOptions>>(),"
        + "provider.GetRequiredService<IOptions<WorkItemRecurrenceOptions>>(),"
        + "provider.GetRequiredService<IClock>()));",
        "services.AddScoped<GetKnowledgeDocumentHandler>(provider=>newGetKnowledgeDocumentHandler("
        + "provider.GetRequiredService<IDocumentRepository<KnowledgeDocument>>(),"
        + "provider.GetRequiredService<IKnowledgeDirectory>(),"
        + "provider.GetRequiredService<ICurrentUser>()));",
        "services.AddScoped<GetKnowledgeVersionHandler>(provider=>newGetKnowledgeVersionHandler("
        + "provider.GetRequiredService<IDocumentRepository<KnowledgeDocument>>(),"
        + "provider.GetRequiredService<IKnowledgeDirectory>(),"
        + "provider.GetRequiredService<ICurrentUser>()));",
        "services.AddScoped<GetKnowledgeLinkOptionsHandler>(provider=>newGetKnowledgeLinkOptionsHandler("
        + "provider.GetRequiredService<IKnowledgeDirectory>(),"
        + "provider.GetRequiredService<ICurrentUser>()));",
        "services.AddScoped<SearchKnowledgeDocumentsHandler>(provider=>newSearchKnowledgeDocumentsHandler("
        + "provider.GetRequiredService<IDocumentRepository<KnowledgeDocument>>(),"
        + "provider.GetRequiredService<IKnowledgeDirectory>(),"
        + "provider.GetRequiredService<ICurrentUser>()));",
        "services.AddScoped<AddKnowledgeCommentHandler>(provider=>newAddKnowledgeCommentHandler("
        + "provider.GetRequiredService<IDocumentRepository<KnowledgeDocument>>(),"
        + "provider.GetRequiredService<IKnowledgeDirectory>(),"
        + "provider.GetRequiredService<IKnowledgeAuditWriter>(),"
        + "provider.GetRequiredService<ICurrentUser>(),"
        + "provider.GetRequiredService<IClock>(),"
        + "provider.GetService<IExpectedVersionAccessor>()));",
        "services.AddScoped<ResolveKnowledgeCommentHandler>(provider=>newResolveKnowledgeCommentHandler("
        + "provider.GetRequiredService<IDocumentRepository<KnowledgeDocument>>(),"
        + "provider.GetRequiredService<IKnowledgeDirectory>(),"
        + "provider.GetRequiredService<IKnowledgeAuditWriter>(),"
        + "provider.GetRequiredService<ICurrentUser>(),"
        + "provider.GetRequiredService<IClock>(),"
        + "provider.GetService<IExpectedVersionAccessor>()));",
        "services.AddScoped<CreateKnowledgeDocumentHandler>(provider=>newCreateKnowledgeDocumentHandler("
        + "provider.GetRequiredService<IDocumentRepository<KnowledgeDocument>>(),"
        + "provider.GetRequiredService<IKnowledgeDirectory>(),"
        + "provider.GetRequiredService<IKnowledgeAuditWriter>(),"
        + "provider.GetRequiredService<ICurrentUser>(),"
        + "provider.GetRequiredService<IClock>()));",
        "services.AddScoped<AddKnowledgeVersionHandler>(provider=>newAddKnowledgeVersionHandler("
        + "provider.GetRequiredService<IDocumentRepository<KnowledgeDocument>>(),"
        + "provider.GetRequiredService<IKnowledgeDirectory>(),"
        + "provider.GetRequiredService<IKnowledgeAuditWriter>(),"
        + "provider.GetRequiredService<ICurrentUser>(),"
        + "provider.GetRequiredService<IClock>(),"
        + "provider.GetService<IExpectedVersionAccessor>()));",
        "services.AddScoped<ArchiveKnowledgeDocumentHandler>(provider=>newArchiveKnowledgeDocumentHandler("
        + "provider.GetRequiredService<IDocumentRepository<KnowledgeDocument>>(),"
        + "provider.GetRequiredService<IKnowledgeDirectory>(),"
        + "provider.GetRequiredService<IKnowledgeAuditWriter>(),"
        + "provider.GetRequiredService<ICurrentUser>(),"
        + "provider.GetRequiredService<IClock>(),"
        + "provider.GetService<IExpectedVersionAccessor>()));",
        "services.AddScoped<RegisterUserHandler>(provider=>newRegisterUserHandler("
        + "provider.GetRequiredService<IUserRepository>(),"
        + "provider.GetRequiredService<IRefreshSessionStore>(),"
        + "provider.GetRequiredService<IDurableTransactionRunner>(),"
        + "provider.GetRequiredService<IPasswordHasher>(),"
        + "provider.GetRequiredService<ITokenIssuer>(),"
        + "provider.GetRequiredService<IOptions<JwtOptions>>(),"
        + "provider.GetRequiredService<IOptions<IdentityBootstrapOptions>>(),"
        + "provider.GetRequiredService<IDistributedLockProvider>(),"
        + "provider.GetRequiredService<IOptions<DistributedLockOptions>>(),"
        + "provider.GetRequiredService<IClock>(),"
        + "provider.GetRequiredService<IRegistrationProvisioningPolicy>(),"
        + "provider.GetRequiredService<ISessionClientContext>()));",
        "services.AddScoped<SearchUsersHandler>(provider=>newSearchUsersHandler("
        + "provider.GetRequiredService<IUserRepository>(),"
        + "provider.GetRequiredService<ICurrentUser>()));",
        "services.AddScoped<CreateOrganizationHandler>(provider=>newCreateOrganizationHandler("
        + "provider.GetRequiredService<IDocumentRepository<OrganizationDocument>>(),"
        + "provider.GetRequiredService<IDistributedLockProvider>(),"
        + "provider.GetRequiredService<IOptions<DistributedLockOptions>>(),"
        + "provider.GetRequiredService<IClock>(),"
        + "provider.GetRequiredService<ICurrentUser>(),"
        + "provider.GetRequiredService<IOrganizationAuditWriter>()));",
        "services.AddScoped<ListOrganizationsHandler>(provider=>newListOrganizationsHandler("
        + "provider.GetRequiredService<IDocumentRepository<OrganizationDocument>>(),"
        + "provider.GetRequiredService<ICurrentUser>()));",
        "services.AddScoped<CreateTeamHandler>(provider=>newCreateTeamHandler("
        + "provider.GetRequiredService<IDocumentRepository<TeamDocument>>(),"
        + "provider.GetRequiredService<ITeamUserDirectory>(),"
        + "provider.GetRequiredService<ITeamOrganizationDirectory>(),"
        + "provider.GetRequiredService<ITeamAuditWriter>(),"
        + "provider.GetRequiredService<IClock>(),"
        + "provider.GetRequiredService<ICurrentUser>()));",
        "services.AddScoped<ListTeamsHandler>(provider=>newListTeamsHandler("
        + "provider.GetRequiredService<IDocumentRepository<TeamDocument>>(),"
        + "provider.GetRequiredService<ITeamOrganizationDirectory>(),"
        + "provider.GetRequiredService<IClock>(),"
        + "provider.GetRequiredService<ICurrentUser>()));",
        "services.AddScoped<CreateProjectHandler>(provider=>newCreateProjectHandler("
        + "provider.GetRequiredService<IDocumentRepository<ProjectDocument>>(),"
        + "provider.GetRequiredService<IProjectMemberDirectory>(),"
        + "provider.GetRequiredService<IProjectOrganizationDirectory>(),"
        + "provider.GetRequiredService<IProjectAuditWriter>(),"
        + "provider.GetRequiredService<IClock>(),"
        + "provider.GetRequiredService<ICurrentUser>()));",
        "services.AddScoped<ListProjectsHandler>(provider=>newListProjectsHandler("
        + "provider.GetRequiredService<IDocumentRepository<ProjectDocument>>(),"
        + "provider.GetRequiredService<IProjectOrganizationDirectory>(),"
        + "provider.GetRequiredService<ICurrentUser>()));",
        "services.AddScoped<CreateBoardHandler>(provider=>newCreateBoardHandler("
        + "provider.GetRequiredService<IDocumentRepository<BoardDocument>>(),"
        + "provider.GetRequiredService<IBoardProjectAccessChecker>(),"
        + "provider.GetRequiredService<IDistributedLockProvider>(),"
        + "provider.GetRequiredService<IOptions<DistributedLockOptions>>(),"
        + "provider.GetRequiredService<IClock>(),"
        + "provider.GetRequiredService<ICurrentUser>(),"
        + "provider.GetRequiredService<IBoardAuditWriter>()));",
        "services.AddScoped<ListBoardsByProjectHandler>(provider=>newListBoardsByProjectHandler("
        + "provider.GetRequiredService<IDocumentRepository<BoardDocument>>(),"
        + "provider.GetRequiredService<IBoardProjectAccessChecker>(),"
        + "provider.GetRequiredService<ICurrentUser>()));",
        "services.AddScoped<UpdateBoardHandler>();",
        "services.AddScoped<ArchiveBoardHandler>();",
        "services.AddScoped<RestoreBoardHandler>();",
        "services.AddScoped<UpdateSwimlaneHandler>();",
        "services.AddScoped<AddColumnHandler>();",
        "services.AddScoped<UpdateColumnHandler>();",
        "services.AddScoped<LoginHandler>();",
        "services.AddScoped<LogoutHandler>();",
        "services.AddScoped<GetMfaStatusHandler>();",
        "services.AddScoped<BeginMfaSetupHandler>();",
        "services.AddScoped<ConfirmMfaSetupHandler>();",
        "services.AddScoped<DisableMfaHandler>();",
        "services.AddScoped<RegenerateMfaRecoveryCodesHandler>();",
        "services.AddScoped<DeactivateAccountHandler>();",
        "services.AddScoped<GetAutomationRunHandler>();",
        "services.AddScoped<ListAutomationRunsHandler>();",
        "services.AddScoped<ReplayAutomationRunHandler>();",
        "services.AddScoped<ListDueAutomationRetriesHandler>();",
        "services.AddScoped<AutomationRunActionExecutor>();",
        "services.AddScoped<ResumeAutomationRunHandler>();",
        "services.AddScoped<ClaimDueSchedulesHandler>();",
        "services.AddScoped<CompleteScheduleClaimHandler>();",
        "services.AddScoped<ExecuteAutomationHandler>();",
        "services.AddScoped<ChangePasswordHandler>();",
        "services.AddScoped<ForgotPasswordHandler>();",
        "services.AddScoped<ResetPasswordHandler>();",
        "services.AddScoped<ListSessionsHandler>();",
        "services.AddScoped<RevokeSessionHandler>();",
        "services.AddScoped<RefreshTokenHandler>();",
        "services.AddScoped<DeleteColumnHandler>();",
        "services.AddScoped<ReorderColumnsHandler>();",
        "services.AddScoped<CreateViewHandler>();",
        "services.AddScoped<UpdateViewHandler>();",
        "services.AddScoped<DeleteViewHandler>();",
        "services.AddScoped<UpsertWorkflowHandler>(provider=>newUpsertWorkflowHandler("
        + "provider.GetRequiredService<IDocumentRepository<WorkflowDefinitionDocument>>(),"
        + "provider.GetRequiredService<IWorkflowProjectAccessChecker>(),"
        + "provider.GetRequiredService<IDistributedLockProvider>(),"
        + "provider.GetRequiredService<IOptions<DistributedLockOptions>>(),"
        + "provider.GetRequiredService<IClock>(),"
        + "provider.GetRequiredService<IWorkflowAuditWriter>(),"
        + "provider.GetRequiredService<IExpectedVersionAccessor>(),"
        + "provider.GetRequiredService<IWorkflowPublicationGuard>()));",
        "services.AddScoped<GetWorkflowHandler>(provider=>newGetWorkflowHandler("
        + "provider.GetRequiredService<IDocumentRepository<WorkflowDefinitionDocument>>(),"
        + "provider.GetRequiredService<IWorkflowProjectAccessChecker>(),"
        + "provider.GetRequiredService<IDistributedLockProvider>(),"
        + "provider.GetRequiredService<IOptions<DistributedLockOptions>>(),"
        + "provider.GetRequiredService<IClock>(),"
        + "provider.GetRequiredService<IExpectedVersionAccessor>()));",
        "services.AddScoped<CreateWorkItemHandler>(provider=>newCreateWorkItemHandler("
        + "provider.GetRequiredService<IDocumentRepository<WorkItemDocument>>(),"
        + "provider.GetRequiredService<IWorkItemNotificationPublisher>(),"
        + "provider.GetRequiredService<IWorkItemAuditPublisher>(),"
        + "provider.GetRequiredService<IClock>(),"
        + "provider.GetRequiredService<ICurrentUser>(),"
        + "provider.GetRequiredService<IProjectPermissionChecker>(),"
        + "provider.GetRequiredService<IWorkItemTeamPolicy>(),"
        + "provider.GetRequiredService<IBoardPlacementPolicy>(),"
        + "provider.GetRequiredService<IDistributedLockProvider>(),"
        + "provider.GetRequiredService<IOptions<DistributedLockOptions>>(),"
        + "provider.GetRequiredService<IWorkItemSearchPublisher>(),"
        + "provider.GetRequiredService<IWorkItemRealtimePublisher>(),"
        + "provider.GetRequiredService<IWorkItemCacheInvalidationPublisher>(),"
        + "provider.GetRequiredService<IWorkItemActivityStore>(),"
        + "provider.GetRequiredService<WorkItemGraphService>(),"
        + "provider.GetService<WorkItemWipProjection>(),"
        + "provider.GetRequiredService<WorkItemRankService>(),"
        + "provider.GetRequiredService<IWorkItemTypeSchemaPolicy>(),"
        + "provider.GetService<WorkItemCollaborationService>(),"
        + "provider.GetService<IWorkItemAutomationEventPublisher>(),"
        + "provider.GetService<IWorkItemAutomationChainContextAccessor>()));",
        "services.AddScoped<IIntakeWorkItemCreator>(provider=>"
        + "newCreateIntakeWorkItemHandler(provider.GetRequiredService<CreateWorkItemHandler>()));",
        "services.AddScoped<SearchWorkItemsHandler>(provider=>newSearchWorkItemsHandler("
        + "provider.GetRequiredService<IDocumentRepository<WorkItemDocument>>(),"
        + "provider.GetRequiredService<ICurrentUser>(),"
        + "provider.GetRequiredService<IProjectPermissionChecker>(),"
        + "provider.GetRequiredService<IWorkItemTypeSchemaPolicy>(),"
        + "provider.GetRequiredService<IWorkItemSearchIndex>(),"
        + "provider.GetRequiredService<IWorkItemActivityStore>(),"
        + "provider.GetRequiredService<IOptions<SearchOptions>>()));",
        "services.AddScoped<BulkMoveWorkItemsHandler>();",
        "services.AddScoped<BulkAssignWorkItemsHandler>();",
        "services.AddScoped<BulkArchiveWorkItemsHandler>();",
        "services.AddScoped<WorkItemBulkJobProcessor>(provider=>newWorkItemBulkJobProcessor("
        + "provider.GetRequiredService<IDocumentRepository<WorkItemBulkJobDocument>>(),"
        + "provider.GetRequiredService<IDocumentRepository<WorkItemBulkJobItemDocument>>(),"
        + "provider.GetRequiredService<IDocumentRepository<WorkItemDocument>>(),"
        + "provider.GetRequiredService<IProjectPermissionChecker>(),"
        + "provider.GetRequiredService<IBoardPlacementPolicy>(),"
        + "provider.GetRequiredService<IWorkItemTeamPolicy>(),"
        + "provider.GetRequiredService<IWorkItemTypeSchemaPolicy>(),"
        + "provider.GetRequiredService<IWorkItemBulkJobEventPublisher>(),"
        + "provider.GetRequiredService<IWorkItemBulkArtifactStorage>(),"
        + "provider.GetRequiredService<IWorkItemAuditPublisher>(),"
        + "provider.GetRequiredService<IOptions<WorkItemBulkJobOptions>>(),"
        + "provider.GetRequiredService<IClock>(),"
        + "provider.GetRequiredService<ICurrentUser>(),"
        + "provider.GetRequiredService<CreateWorkItemHandler>(),"
        + "provider.GetRequiredService<MoveWorkItemHandler>(),"
        + "provider.GetRequiredService<AssignWorkItemHandler>(),"
        + "provider.GetRequiredService<ArchiveWorkItemHandler>()));",
        "services.AddScoped<IAutomationActionExecutor>(provider=>newAutomationWorkItemActionExecutor("
        + "provider.GetRequiredService<GetWorkItemHandler>(),"
        + "provider.GetRequiredService<AssignWorkItemHandler>(),"
        + "provider.GetRequiredService<ClearAssigneeHandler>(),"
        + "provider.GetRequiredService<AddLabelHandler>(),"
        + "provider.GetRequiredService<RemoveLabelHandler>(),"
        + "provider.GetRequiredService<UpdateWorkItemHandler>(),"
        + "provider.GetRequiredService<AddCommentHandler>(),"
        + "provider.GetRequiredService<IWorkItemAutomationChainContextAccessor>()));",
        "services.AddScoped<DashboardRenderer>(provider=>newDashboardRenderer("
        + "provider.GetRequiredService<DashboardService>(),"
        + "provider.GetRequiredService<ProjectSummaryHandler>(),"
        + "provider.GetRequiredService<StatusDistributionHandler>(),"
        + "provider.GetRequiredService<UserWorkloadHandler>(),"
        + "provider.GetRequiredService<DueDateRisksHandler>(),"
        + "provider.GetRequiredService<FlowTimeHandler>(),"
        + "provider.GetRequiredService<CompletionRateHandler>(),"
        + "provider.GetRequiredService<TeamPerformanceHandler>(),"
        + "provider.GetRequiredService<IClock>()));",
        "services.AddScoped<GetWorkItemHandler>(provider=>newGetWorkItemHandler("
        + "provider.GetRequiredService<IDocumentRepository<WorkItemDocument>>(),"
        + "provider.GetRequiredService<ICurrentUser>(),"
        + "provider.GetRequiredService<IProjectPermissionChecker>(),"
        + "provider.GetRequiredService<IWorkItemActivityStore>()));",
        "services.AddScoped<ArchiveWorkItemHandler>(provider=>newArchiveWorkItemHandler("
        + "provider.GetRequiredService<IDocumentRepository<WorkItemDocument>>(),"
        + "provider.GetRequiredService<IWorkItemAuditPublisher>(),"
        + "provider.GetRequiredService<IClock>(),"
        + "provider.GetRequiredService<ICurrentUser>(),"
        + "provider.GetRequiredService<IProjectPermissionChecker>(),"
        + "provider.GetRequiredService<IDistributedLockProvider>(),"
        + "provider.GetRequiredService<IOptions<DistributedLockOptions>>(),"
        + "provider.GetRequiredService<IWorkItemSearchPublisher>(),"
        + "provider.GetRequiredService<IWorkItemRealtimePublisher>(),"
        + "provider.GetRequiredService<IWorkItemCacheInvalidationPublisher>(),"
        + "provider.GetRequiredService<IWorkItemActivityStore>(),"
        + "provider.GetService<IExpectedVersionAccessor>(),"
        + "provider.GetService<WorkItemWipProjection>(),"
        + "provider.GetService<WorkItemCollaborationService>()));",
        "services.AddScoped<RestoreWorkItemHandler>(provider=>newRestoreWorkItemHandler("
        + "provider.GetRequiredService<IDocumentRepository<WorkItemDocument>>(),"
        + "provider.GetRequiredService<IWorkItemAuditPublisher>(),"
        + "provider.GetRequiredService<IClock>(),"
        + "provider.GetRequiredService<ICurrentUser>(),"
        + "provider.GetRequiredService<IProjectPermissionChecker>(),"
        + "provider.GetRequiredService<IBoardPlacementPolicy>(),"
        + "provider.GetRequiredService<IDistributedLockProvider>(),"
        + "provider.GetRequiredService<IOptions<DistributedLockOptions>>(),"
        + "provider.GetRequiredService<IWorkItemSearchPublisher>(),"
        + "provider.GetRequiredService<IWorkItemRealtimePublisher>(),"
        + "provider.GetRequiredService<IWorkItemCacheInvalidationPublisher>(),"
        + "provider.GetRequiredService<IWorkItemActivityStore>(),"
        + "provider.GetService<IExpectedVersionAccessor>(),"
        + "provider.GetService<WorkItemWipProjection>(),"
        + "provider.GetRequiredService<WorkItemRankService>(),"
        + "provider.GetService<WorkItemCollaborationService>()));",
        "services.AddScoped<AddLabelHandler>(provider=>newAddLabelHandler("
        + "provider.GetRequiredService<IDocumentRepository<WorkItemDocument>>(),"
        + "provider.GetRequiredService<IClock>(),"
        + "provider.GetRequiredService<ICurrentUser>(),"
        + "provider.GetRequiredService<IProjectPermissionChecker>(),"
        + "provider.GetRequiredService<IWorkItemSearchPublisher>(),"
        + "provider.GetRequiredService<IWorkItemActivityStore>(),"
        + "provider.GetService<IExpectedVersionAccessor>(),"
        + "provider.GetService<WorkItemCollaborationService>(),"
        + "provider.GetService<IWorkItemAutomationEventPublisher>(),"
        + "provider.GetService<IWorkItemAutomationChainContextAccessor>()));",
        "services.AddScoped<RemoveLabelHandler>(provider=>newRemoveLabelHandler("
        + "provider.GetRequiredService<IDocumentRepository<WorkItemDocument>>(),"
        + "provider.GetRequiredService<IClock>(),"
        + "provider.GetRequiredService<ICurrentUser>(),"
        + "provider.GetRequiredService<IProjectPermissionChecker>(),"
        + "provider.GetRequiredService<IWorkItemSearchPublisher>(),"
        + "provider.GetRequiredService<IWorkItemActivityStore>(),"
        + "provider.GetService<IExpectedVersionAccessor>(),"
        + "provider.GetService<WorkItemCollaborationService>(),"
        + "provider.GetService<IWorkItemAutomationEventPublisher>(),"
        + "provider.GetService<IWorkItemAutomationChainContextAccessor>()));",
        "services.AddScoped<AddChecklistItemHandler>(provider=>newAddChecklistItemHandler("
        + "provider.GetRequiredService<IDocumentRepository<WorkItemDocument>>(),"
        + "provider.GetRequiredService<IClock>(),"
        + "provider.GetRequiredService<ICurrentUser>(),"
        + "provider.GetRequiredService<IProjectPermissionChecker>(),"
        + "provider.GetRequiredService<IWorkItemActivityStore>(),"
        + "provider.GetService<IExpectedVersionAccessor>(),"
        + "provider.GetService<WorkItemCollaborationService>()));",
        "services.AddScoped<CompleteChecklistItemHandler>(provider=>newCompleteChecklistItemHandler("
        + "provider.GetRequiredService<IDocumentRepository<WorkItemDocument>>(),"
        + "provider.GetRequiredService<IClock>(),"
        + "provider.GetRequiredService<ICurrentUser>(),"
        + "provider.GetRequiredService<IProjectPermissionChecker>(),"
        + "provider.GetRequiredService<IWorkItemActivityStore>(),"
        + "provider.GetService<IExpectedVersionAccessor>(),"
        + "provider.GetService<WorkItemCollaborationService>()));",
        "services.AddScoped<AddWorkLogHandler>(provider=>newAddWorkLogHandler("
        + "provider.GetRequiredService<IDocumentRepository<WorkItemDocument>>(),"
        + "provider.GetRequiredService<IClock>(),"
        + "provider.GetRequiredService<ICurrentUser>(),"
        + "provider.GetRequiredService<IProjectPermissionChecker>(),"
        + "provider.GetRequiredService<IWorkItemActivityStore>(),"
        + "provider.GetService<IExpectedVersionAccessor>(),"
        + "provider.GetRequiredService<IWorkItemCacheInvalidationPublisher>(),"
        + "provider.GetService<WorkItemCollaborationService>()));",
        "services.AddScoped<SetPlanningHandler>(provider=>newSetPlanningHandler("
        + "provider.GetRequiredService<IDocumentRepository<WorkItemDocument>>(),"
        + "provider.GetRequiredService<IClock>(),"
        + "provider.GetRequiredService<ICurrentUser>(),"
        + "provider.GetRequiredService<IProjectPermissionChecker>(),"
        + "provider.GetService<IWorkItemSprintPolicy>(),"
        + "provider.GetRequiredService<IWorkItemSearchPublisher>(),"
        + "provider.GetRequiredService<IWorkItemActivityStore>(),"
        + "provider.GetService<IExpectedVersionAccessor>(),"
        + "provider.GetRequiredService<IWorkItemCacheInvalidationPublisher>(),"
        + "provider.GetService<WorkItemCollaborationService>()));",
        "services.AddScoped<MoveWorkItemHandler>(provider=>newMoveWorkItemHandler("
        + "provider.GetRequiredService<IDocumentRepository<WorkItemDocument>>(),"
        + "provider.GetRequiredService<IWorkItemAuditPublisher>(),"
        + "provider.GetRequiredService<IClock>(),"
        + "provider.GetRequiredService<ICurrentUser>(),"
        + "provider.GetRequiredService<IProjectPermissionChecker>(),"
        + "provider.GetRequiredService<IWorkflowPolicy>(),"
        + "provider.GetRequiredService<IBoardPlacementPolicy>(),"
        + "provider.GetRequiredService<IDistributedLockProvider>(),"
        + "provider.GetRequiredService<IOptions<DistributedLockOptions>>(),"
        + "provider.GetRequiredService<IWorkItemSearchPublisher>(),"
        + "provider.GetRequiredService<IWorkItemRealtimePublisher>(),"
        + "provider.GetRequiredService<IWorkItemCacheInvalidationPublisher>(),"
        + "provider.GetRequiredService<IWorkItemActivityStore>(),"
        + "provider.GetRequiredService<WorkItemGraphService>(),"
        + "provider.GetService<IExpectedVersionAccessor>(),"
        + "provider.GetService<WorkItemWipProjection>(),"
        + "provider.GetRequiredService<WorkItemRankService>(),"
        + "provider.GetService<WorkItemCollaborationService>(),"
        + "provider.GetService<IWorkItemAutomationEventPublisher>(),"
        + "provider.GetService<IWorkItemAutomationChainContextAccessor>()));",
        "services.AddScoped<ReorderWorkItemHandler>(provider=>newReorderWorkItemHandler("
        + "provider.GetRequiredService<IDocumentRepository<WorkItemDocument>>(),"
        + "provider.GetRequiredService<IWorkItemAuditPublisher>(),"
        + "provider.GetRequiredService<IClock>(),"
        + "provider.GetRequiredService<ICurrentUser>(),"
        + "provider.GetRequiredService<IProjectPermissionChecker>(),"
        + "provider.GetRequiredService<IDistributedLockProvider>(),"
        + "provider.GetRequiredService<IOptions<DistributedLockOptions>>(),"
        + "provider.GetRequiredService<IWorkItemRealtimePublisher>(),"
        + "provider.GetRequiredService<IWorkItemActivityStore>(),"
        + "provider.GetService<IExpectedVersionAccessor>(),"
        + "provider.GetRequiredService<WorkItemRankService>(),"
        + "provider.GetService<WorkItemCollaborationService>()));",
        "services.AddScoped<AddCommentHandler>(provider=>newAddCommentHandler("
        + "provider.GetRequiredService<IDocumentRepository<WorkItemDocument>>(),"
        + "provider.GetRequiredService<IWorkItemNotificationPublisher>(),"
        + "provider.GetRequiredService<IWorkItemAuditPublisher>(),"
        + "provider.GetRequiredService<IClock>(),"
        + "provider.GetRequiredService<ICurrentUser>(),"
        + "provider.GetRequiredService<IProjectPermissionChecker>(),"
        + "provider.GetRequiredService<IWorkItemActivityStore>(),"
        + "provider.GetService<IExpectedVersionAccessor>(),"
        + "provider.GetService<WorkItemCollaborationService>(),"
        + "provider.GetService<IWorkItemAutomationEventPublisher>(),"
        + "provider.GetService<IWorkItemAutomationChainContextAccessor>()));",
        "services.AddScoped<EditCommentHandler>(provider=>newEditCommentHandler("
        + "provider.GetRequiredService<IDocumentRepository<WorkItemDocument>>(),"
        + "provider.GetRequiredService<IWorkItemAuditPublisher>(),"
        + "provider.GetRequiredService<IClock>(),"
        + "provider.GetRequiredService<ICurrentUser>(),"
        + "provider.GetRequiredService<IProjectPermissionChecker>(),"
        + "provider.GetRequiredService<IWorkItemActivityStore>(),"
        + "provider.GetService<IExpectedVersionAccessor>(),"
        + "provider.GetService<WorkItemCollaborationService>()));",
        "services.AddScoped<DeleteCommentHandler>(provider=>newDeleteCommentHandler("
        + "provider.GetRequiredService<IDocumentRepository<WorkItemDocument>>(),"
        + "provider.GetRequiredService<IWorkItemAuditPublisher>(),"
        + "provider.GetRequiredService<ICurrentUser>(),"
        + "provider.GetRequiredService<IProjectPermissionChecker>(),"
        + "provider.GetRequiredService<IWorkItemActivityStore>(),"
        + "provider.GetService<IExpectedVersionAccessor>(),"
        + "provider.GetService<WorkItemCollaborationService>()));",
        "services.AddScoped<SetParentHandler>(provider=>newSetParentHandler("
        + "provider.GetRequiredService<IDocumentRepository<WorkItemDocument>>(),"
        + "provider.GetRequiredService<IWorkItemAuditPublisher>(),"
        + "provider.GetRequiredService<IClock>(),"
        + "provider.GetRequiredService<ICurrentUser>(),"
        + "provider.GetRequiredService<IProjectPermissionChecker>(),"
        + "provider.GetRequiredService<IWorkItemTypeSchemaPolicy>(),"
        + "provider.GetRequiredService<IDistributedLockProvider>(),"
        + "provider.GetRequiredService<IOptions<DistributedLockOptions>>(),"
        + "provider.GetRequiredService<IWorkItemActivityStore>(),"
        + "provider.GetRequiredService<WorkItemGraphService>(),"
        + "provider.GetService<IExpectedVersionAccessor>(),"
        + "provider.GetService<WorkItemCollaborationService>()));",
        "services.AddScoped<LinkWorkItemHandler>(provider=>newLinkWorkItemHandler("
        + "provider.GetRequiredService<IDocumentRepository<WorkItemDocument>>(),"
        + "provider.GetRequiredService<IWorkItemAuditPublisher>(),"
        + "provider.GetRequiredService<IClock>(),"
        + "provider.GetRequiredService<ICurrentUser>(),"
        + "provider.GetRequiredService<IProjectPermissionChecker>(),"
        + "provider.GetRequiredService<IDistributedLockProvider>(),"
        + "provider.GetRequiredService<IOptions<DistributedLockOptions>>(),"
        + "provider.GetRequiredService<IWorkItemActivityStore>(),"
        + "provider.GetRequiredService<WorkItemGraphService>(),"
        + "provider.GetService<IExpectedVersionAccessor>(),"
        + "provider.GetService<WorkItemCollaborationService>()));",
        "services.AddScoped<UnlinkWorkItemHandler>(provider=>newUnlinkWorkItemHandler("
        + "provider.GetRequiredService<IDocumentRepository<WorkItemDocument>>(),"
        + "provider.GetRequiredService<IWorkItemAuditPublisher>(),"
        + "provider.GetRequiredService<IClock>(),"
        + "provider.GetRequiredService<ICurrentUser>(),"
        + "provider.GetRequiredService<IProjectPermissionChecker>(),"
        + "provider.GetRequiredService<IDistributedLockProvider>(),"
        + "provider.GetRequiredService<IOptions<DistributedLockOptions>>(),"
        + "provider.GetRequiredService<IWorkItemActivityStore>(),"
        + "provider.GetRequiredService<WorkItemGraphService>(),"
        + "provider.GetService<IExpectedVersionAccessor>(),"
        + "provider.GetService<WorkItemCollaborationService>()));",
        "services.AddScoped<OpenAttachmentHandler>(provider=>newOpenAttachmentHandler("
        + "provider.GetRequiredService<IDocumentRepository<WorkItemDocument>>(),"
        + "provider.GetRequiredService<ICurrentUser>(),"
        + "provider.GetRequiredService<IProjectPermissionChecker>(),"
        + "provider.GetRequiredService<IWorkItemActivityStore>(),"
        + "provider.GetRequiredService<IAttachmentStorage>()));",
        "services.AddScoped<UploadAttachmentHandler>(provider=>newUploadAttachmentHandler("
        + "provider.GetRequiredService<IDocumentRepository<WorkItemDocument>>(),"
        + "provider.GetRequiredService<IWorkItemAuditPublisher>(),"
        + "provider.GetRequiredService<IClock>(),"
        + "provider.GetRequiredService<ICurrentUser>(),"
        + "provider.GetRequiredService<IProjectPermissionChecker>(),"
        + "provider.GetRequiredService<IAttachmentStorage>(),"
        + "provider.GetRequiredService<IDistributedLockProvider>(),"
        + "provider.GetRequiredService<IOptions<DistributedLockOptions>>(),"
        + "provider.GetRequiredService<IWorkItemActivityStore>(),"
        + "provider.GetService<IExpectedVersionAccessor>(),"
        + "provider.GetService<WorkItemCollaborationService>(),"
        + "provider.GetService<ILogger<WorkItemService>>()));",
        "services.AddScoped<DeleteAttachmentHandler>(provider=>newDeleteAttachmentHandler("
        + "provider.GetRequiredService<IDocumentRepository<WorkItemDocument>>(),"
        + "provider.GetRequiredService<IWorkItemAuditPublisher>(),"
        + "provider.GetRequiredService<ICurrentUser>(),"
        + "provider.GetRequiredService<IProjectPermissionChecker>(),"
        + "provider.GetRequiredService<IAttachmentStorage>(),"
        + "provider.GetRequiredService<IDistributedLockProvider>(),"
        + "provider.GetRequiredService<IOptions<DistributedLockOptions>>(),"
        + "provider.GetRequiredService<IWorkItemActivityStore>(),"
        + "provider.GetService<IExpectedVersionAccessor>(),"
        + "provider.GetService<WorkItemCollaborationService>(),"
        + "provider.GetService<ILogger<WorkItemService>>()));",
        "services.AddScoped<SendDueDateRemindersHandler>(provider=>newSendDueDateRemindersHandler("
        + "provider.GetRequiredService<IDocumentRepository<WorkItemDocument>>(),"
        + "provider.GetRequiredService<IWorkItemNotificationPublisher>(),"
        + "provider.GetRequiredService<IClock>(),"
        + "provider.GetRequiredService<IProjectPermissionChecker>(),"
        + "provider.GetRequiredService<IDistributedLockProvider>(),"
        + "provider.GetRequiredService<IOptions<DistributedLockOptions>>(),"
        + "provider.GetRequiredService<IWorkItemActivityStore>(),"
        + "provider.GetService<IExpectedVersionAccessor>()));",
        "services.AddScoped<ProjectSummaryHandler>(provider=>newProjectSummaryHandler("
        + "provider.GetRequiredService<IDocumentRepository<WorkItemDocument>>(),"
        + "provider.GetRequiredService<IClock>(),"
        + "provider.GetRequiredService<ICurrentUser>(),"
        + "provider.GetRequiredService<IProjectPermissionChecker>(),"
        + "provider.GetRequiredService<IWorkItemReadModelCache>(),"
        + "provider.GetRequiredService<IOptions<WorkItemReadModelCacheOptions>>()));",
        "services.AddScoped<StatusDistributionHandler>(provider=>newStatusDistributionHandler("
        + "provider.GetRequiredService<IDocumentRepository<WorkItemDocument>>(),"
        + "provider.GetRequiredService<ICurrentUser>(),"
        + "provider.GetRequiredService<IProjectPermissionChecker>(),"
        + "provider.GetRequiredService<IWorkItemReadModelCache>(),"
        + "provider.GetRequiredService<IOptions<WorkItemReadModelCacheOptions>>()));",
        "services.AddScoped<UserWorkloadHandler>(provider=>newUserWorkloadHandler("
        + "provider.GetRequiredService<IDocumentRepository<WorkItemDocument>>(),"
        + "provider.GetRequiredService<IClock>(),"
        + "provider.GetRequiredService<ICurrentUser>(),"
        + "provider.GetRequiredService<IProjectPermissionChecker>(),"
        + "provider.GetRequiredService<IWorkItemReadModelCache>(),"
        + "provider.GetRequiredService<IOptions<WorkItemReadModelCacheOptions>>(),"
        + "provider.GetRequiredService<IWorkItemActivityStore>()));",
        "services.AddScoped<DueDateRisksHandler>(provider=>newDueDateRisksHandler("
        + "provider.GetRequiredService<IDocumentRepository<WorkItemDocument>>(),"
        + "provider.GetRequiredService<IClock>(),"
        + "provider.GetRequiredService<ICurrentUser>(),"
        + "provider.GetRequiredService<IProjectPermissionChecker>(),"
        + "provider.GetRequiredService<IWorkItemReadModelCache>(),"
        + "provider.GetRequiredService<IOptions<WorkItemReadModelCacheOptions>>()));",
        "services.AddScoped<FlowTimeHandler>(provider=>newFlowTimeHandler("
        + "provider.GetRequiredService<IDocumentRepository<WorkItemDocument>>(),"
        + "provider.GetRequiredService<IClock>(),"
        + "provider.GetRequiredService<ICurrentUser>(),"
        + "provider.GetRequiredService<IProjectPermissionChecker>(),"
        + "provider.GetRequiredService<IWorkItemReadModelCache>(),"
        + "provider.GetRequiredService<IOptions<WorkItemReadModelCacheOptions>>(),"
        + "provider.GetRequiredService<IWorkItemActivityStore>()));",
        "services.AddScoped<CompletionRateHandler>(provider=>newCompletionRateHandler("
        + "provider.GetRequiredService<IDocumentRepository<WorkItemDocument>>(),"
        + "provider.GetRequiredService<IClock>(),"
        + "provider.GetRequiredService<ICurrentUser>(),"
        + "provider.GetRequiredService<IProjectPermissionChecker>(),"
        + "provider.GetRequiredService<IWorkItemReadModelCache>(),"
        + "provider.GetRequiredService<IOptions<WorkItemReadModelCacheOptions>>()));",
        "services.AddScoped<TeamPerformanceHandler>(provider=>newTeamPerformanceHandler("
        + "provider.GetRequiredService<IDocumentRepository<WorkItemDocument>>(),"
        + "provider.GetRequiredService<IClock>(),"
        + "provider.GetRequiredService<ICurrentUser>(),"
        + "provider.GetRequiredService<IProjectPermissionChecker>(),"
        + "provider.GetRequiredService<IWorkItemTeamPolicy>(),"
        + "provider.GetRequiredService<IWorkItemReadModelCache>(),"
        + "provider.GetRequiredService<IOptions<WorkItemReadModelCacheOptions>>(),"
        + "provider.GetRequiredService<IWorkItemActivityStore>()));",
        "services.AddScoped<ClearAssigneeHandler>(provider=>newClearAssigneeHandler("
        + "provider.GetRequiredService<IDocumentRepository<WorkItemDocument>>(),"
        + "provider.GetRequiredService<IWorkItemAuditPublisher>(),"
        + "provider.GetRequiredService<IClock>(),"
        + "provider.GetRequiredService<ICurrentUser>(),"
        + "provider.GetRequiredService<IProjectPermissionChecker>(),"
        + "provider.GetRequiredService<IWorkItemSearchPublisher>(),"
        + "provider.GetRequiredService<IWorkItemRealtimePublisher>(),"
        + "provider.GetRequiredService<IWorkItemCacheInvalidationPublisher>(),"
        + "provider.GetRequiredService<IWorkItemActivityStore>(),"
        + "provider.GetService<IExpectedVersionAccessor>(),"
        + "provider.GetService<WorkItemCollaborationService>(),"
        + "provider.GetService<IWorkItemAutomationEventPublisher>(),"
        + "provider.GetService<IWorkItemAutomationChainContextAccessor>()));",
        "services.AddScoped<AssignWorkItemHandler>(provider=>newAssignWorkItemHandler("
        + "provider.GetRequiredService<IDocumentRepository<WorkItemDocument>>(),"
        + "provider.GetRequiredService<IWorkItemAuditPublisher>(),"
        + "provider.GetRequiredService<IClock>(),"
        + "provider.GetRequiredService<ICurrentUser>(),"
        + "provider.GetRequiredService<IProjectPermissionChecker>(),"
        + "provider.GetRequiredService<IWorkItemSearchPublisher>(),"
        + "provider.GetRequiredService<IWorkItemRealtimePublisher>(),"
        + "provider.GetRequiredService<IWorkItemCacheInvalidationPublisher>(),"
        + "provider.GetRequiredService<IWorkItemActivityStore>(),"
        + "provider.GetService<IExpectedVersionAccessor>(),"
        + "provider.GetService<WorkItemCollaborationService>(),"
        + "provider.GetRequiredService<IWorkItemTeamPolicy>(),"
        + "provider.GetRequiredService<IWorkItemNotificationPublisher>()));",
        "services.AddScoped<SetWorkItemTeamHandler>(provider=>newSetWorkItemTeamHandler("
        + "provider.GetRequiredService<IDocumentRepository<WorkItemDocument>>(),"
        + "provider.GetRequiredService<IWorkItemAuditPublisher>(),"
        + "provider.GetRequiredService<IClock>(),"
        + "provider.GetRequiredService<ICurrentUser>(),"
        + "provider.GetRequiredService<IProjectPermissionChecker>(),"
        + "provider.GetRequiredService<IWorkItemSearchPublisher>(),"
        + "provider.GetRequiredService<IWorkItemRealtimePublisher>(),"
        + "provider.GetRequiredService<IWorkItemCacheInvalidationPublisher>(),"
        + "provider.GetRequiredService<IWorkItemActivityStore>(),"
        + "provider.GetService<IExpectedVersionAccessor>(),"
        + "provider.GetService<WorkItemCollaborationService>(),"
        + "provider.GetRequiredService<IWorkItemTeamPolicy>()));",
        "services.AddScoped<SetCustomFieldsHandler>(provider=>newSetCustomFieldsHandler("
        + "provider.GetRequiredService<IDocumentRepository<WorkItemDocument>>(),"
        + "provider.GetRequiredService<IWorkItemAuditPublisher>(),"
        + "provider.GetRequiredService<IClock>(),"
        + "provider.GetRequiredService<ICurrentUser>(),"
        + "provider.GetRequiredService<IProjectPermissionChecker>(),"
        + "provider.GetRequiredService<IWorkItemTypeSchemaPolicy>(),"
        + "provider.GetRequiredService<IWorkItemSearchPublisher>(),"
        + "provider.GetRequiredService<IWorkItemRealtimePublisher>(),"
        + "provider.GetRequiredService<IWorkItemCacheInvalidationPublisher>(),"
        + "provider.GetRequiredService<IWorkItemActivityStore>(),"
        + "provider.GetService<IExpectedVersionAccessor>(),"
        + "provider.GetService<WorkItemCollaborationService>(),"
        + "provider.GetService<IWorkItemAutomationEventPublisher>(),"
        + "provider.GetService<IWorkItemAutomationChainContextAccessor>()));",
        "services.AddScoped<RequestApprovalHandler>(provider=>newRequestApprovalHandler("
        + "provider.GetRequiredService<IDocumentRepository<WorkItemDocument>>(),"
        + "provider.GetRequiredService<IWorkItemNotificationPublisher>(),"
        + "provider.GetRequiredService<IWorkItemAuditPublisher>(),"
        + "provider.GetRequiredService<IClock>(),"
        + "provider.GetRequiredService<ICurrentUser>(),"
        + "provider.GetRequiredService<IProjectPermissionChecker>(),"
        + "provider.GetRequiredService<IWorkflowPolicy>(),"
        + "provider.GetRequiredService<IWorkItemActivityStore>(),"
        + "provider.GetService<IExpectedVersionAccessor>(),"
        + "provider.GetService<WorkItemCollaborationService>()));",
        "services.AddScoped<UpdateWorkItemHandler>(provider=>newUpdateWorkItemHandler("
        + "provider.GetRequiredService<IDocumentRepository<WorkItemDocument>>(),"
        + "provider.GetRequiredService<IWorkItemAuditPublisher>(),"
        + "provider.GetRequiredService<IClock>(),"
        + "provider.GetRequiredService<ICurrentUser>(),"
        + "provider.GetRequiredService<IProjectPermissionChecker>(),"
        + "provider.GetRequiredService<IWorkItemSearchPublisher>(),"
        + "provider.GetRequiredService<IWorkItemRealtimePublisher>(),"
        + "provider.GetRequiredService<IWorkItemCacheInvalidationPublisher>(),"
        + "provider.GetRequiredService<IWorkItemActivityStore>(),"
        + "provider.GetService<IExpectedVersionAccessor>(),"
        + "provider.GetService<WorkItemCollaborationService>(),"
        + "provider.GetService<IWorkItemAutomationEventPublisher>(),"
        + "provider.GetService<IWorkItemAutomationChainContextAccessor>()));",
        "services.AddScoped<DecideApprovalHandler>(provider=>newDecideApprovalHandler("
        + "provider.GetRequiredService<IDocumentRepository<WorkItemDocument>>(),"
        + "provider.GetRequiredService<IWorkItemNotificationPublisher>(),"
        + "provider.GetRequiredService<IWorkItemAuditPublisher>(),"
        + "provider.GetRequiredService<IClock>(),"
        + "provider.GetRequiredService<ICurrentUser>(),"
        + "provider.GetRequiredService<IProjectPermissionChecker>(),"
        + "provider.GetRequiredService<IWorkItemActivityStore>(),"
        + "provider.GetService<IExpectedVersionAccessor>(),"
        + "provider.GetService<WorkItemCollaborationService>()));",
        "services.AddScoped<ListNotificationsHandler>(provider=>newListNotificationsHandler("
        + "provider.GetRequiredService<IDocumentRepository<NotificationDocument>>(),"
        + "provider.GetRequiredService<ICurrentUser>()));",
        "services.AddScoped<MarkNotificationAsReadHandler>(provider=>newMarkNotificationAsReadHandler("
        + "provider.GetRequiredService<IDocumentRepository<NotificationDocument>>(),"
        + "provider.GetRequiredService<ICurrentUser>()));",
        "services.AddScoped<WriteAuditLogHandler>(provider=>newWriteAuditLogHandler("
        + "provider.GetRequiredService<IDocumentRepository<AuditLogDocument>>(),"
        + "provider.GetRequiredService<IClock>(),"
        + "provider.GetRequiredService<ICurrentUser>(),"
        + "provider.GetRequiredService<IAuditRequestContext>(),"
        + "provider.GetRequiredService<IOptions<AuditOptions>>(),"
        + "provider.GetService<IAuditTenantResolver>(),"
        + "provider.GetService<IDistributedLockProvider>()));",
        "services.AddScoped<QueryAuditLogHandler>(provider=>newQueryAuditLogHandler("
        + "provider.GetRequiredService<IDocumentRepository<AuditLogDocument>>(),"
        + "provider.GetRequiredService<IAuditAccessChecker>()));",
        "services.AddScoped<CapacityPlanAccessPolicy>();",
        "services.AddScoped<ArchiveCapacityPlanHandler>();",
        "services.AddScoped<GetCapacityPlanHandler>();",
        "services.AddScoped<ListCapacityPlansHandler>();",
        "services.AddScoped<ShareCapacityPlanHandler>();",
        "services.AddScoped<SaveCapacityPlanHandler>();",
        "services.AddScoped<GetCapacitySnapshotHandler>();",
        "services.AddScoped<PreviewScenarioHandler>();",
        "services.AddScoped<ListWebhookSubscriptionsHandler>();",
        "services.AddScoped<GetWebhookSubscriptionHandler>();",
        "services.AddScoped<GetWebhookDeliveryMetricsHandler>();",
        "services.AddScoped<ListWebhookDeliveriesHandler>();",
        "services.AddScoped<GetWebhookDeliveryHandler>();",
        "services.AddScoped<ReplayWebhookDeliveryHandler>();",
        "services.AddScoped<SetSubscriptionStateHandler>();",
        "services.AddScoped<UpdateSubscriptionHandler>();",
        "services.AddScoped<CreateSubscriptionHandler>();",
        "services.AddScoped<RotateSecretHandler>();",
        "services.AddScoped<QueueTestDeliveryHandler>();",
        "services.AddScoped<QueueDeliveryHandler>();",
        "services.AddScoped<DispatchDeliveriesHandler>();",
        "services.AddScoped<IWorkItemWebhookDelivery>(provider=>newWorkItemWebhookDeliveryAdapter("
        + "provider.GetRequiredService<QueueDeliveryHandler>()));",
        "services.AddScoped<IDurableEventHandler,DevelopmentWebhookProcessingDurableHandler>();",
        "services.AddScoped<CheckProviderHealthHandler>();",
        "services.AddScoped<ListRepositoriesHandler>();",
        "services.AddScoped<ListConnectionsHandler>();",
        "services.AddScoped<GetConnectionHandler>();",
        "services.AddScoped<CreateConnectionHandler>();",
        "services.AddScoped<RotateCredentialHandler>();",
        "services.AddScoped<RotateWebhookSecretHandler>();",
        "services.AddScoped<DisconnectConnectionHandler>();",
        "services.AddScoped<DeleteConnectionHandler>();",
        "services.AddScoped<ListConnectionMappingsHandler>();",
        "services.AddScoped<CreateMappingHandler>();",
        "services.AddScoped<DeleteMappingHandler>();",
        "services.AddScoped<ListWorkItemMappingsHandler>();",
        "services.AddScoped<ListWorkItemLinksHandler>();",
        "services.AddScoped<CreateWorkItemLinkHandler>();",
        "services.AddScoped<DeleteWorkItemLinkHandler>();",
        "services.AddScoped<ReceiveWebhookHandler>();",
        "services.AddScoped<ApplyWebhookLinksHandler>();",
        "services.AddScoped<ProcessWebhookHandler>();"
    ];

    private static readonly string[] ReplacedVerticalSliceEndpointMappings =
    [
        "group.MapPost(\"/{portfolioId}/dependencies\",async(stringportfolioId,SavePortfolioDependencyRequestrequest,[FromServices]PortfolioServiceservice,HttpContexthttp,CancellationTokenct)=>"
        + "Ok(awaitservice.SaveDependencyAsync(portfolioId,null,request,CorrelationId(http),ct),http));",
        "group.MapPut(\"/{portfolioId}/dependencies/{dependencyId}\",async(stringportfolioId,stringdependencyId,SavePortfolioDependencyRequestrequest,[FromServices]PortfolioServiceservice,HttpContexthttp,CancellationTokenct)=>"
        + "Ok(awaitservice.SaveDependencyAsync(portfolioId,dependencyId,request,CorrelationId(http),ct),http));",
        "group.MapPost(\"/{portfolioId}/initiatives\",async(stringportfolioId,SaveInitiativeRequestrequest,[FromServices]PortfolioServiceservice,HttpContexthttp,CancellationTokenct)=>"
        + "Ok(awaitservice.SaveInitiativeAsync(portfolioId,null,request,CorrelationId(http),ct),http));",
        "group.MapPut(\"/{portfolioId}/initiatives/{initiativeId}\",async(stringportfolioId,stringinitiativeId,SaveInitiativeRequestrequest,[FromServices]PortfolioServiceservice,HttpContexthttp,CancellationTokenct)=>"
        + "Ok(awaitservice.SaveInitiativeAsync(portfolioId,initiativeId,request,CorrelationId(http),ct),http));",
        "group.MapPost(\"/{portfolioId}/initiatives/{initiativeId}/status-updates\",async(stringportfolioId,stringinitiativeId,AddInitiativeStatusUpdateRequestrequest,[FromServices]PortfolioServiceservice,HttpContexthttp,CancellationTokenct)=>"
        + "Ok(awaitservice.AddStatusUpdateAsync(portfolioId,initiativeId,request,CorrelationId(http),ct),http));",
        "group.MapPost(\"\",async(SavePortfolioRequestrequest,[FromServices]PortfolioServiceservice,HttpContexthttp,CancellationTokenct)=>"
        + "Ok(awaitservice.SaveAsync(null,request,CorrelationId(http),ct),http));",
        "group.MapPut(\"/{portfolioId}\",async(stringportfolioId,SavePortfolioRequestrequest,[FromServices]PortfolioServiceservice,HttpContexthttp,CancellationTokenct)=>"
        + "Ok(awaitservice.SaveAsync(portfolioId,request,CorrelationId(http),ct),http));",
        "group.MapDelete(\"/{portfolioId}\",async(stringportfolioId,[FromServices]PortfolioServiceservice,HttpContexthttp,CancellationTokenct)=>"
        + "{awaitservice.ArchiveAsync(portfolioId,CorrelationId(http),ct);returnOk(new{archived=true},http);});",
        "group.MapGet(\"\",async(bool?includeArchived,int?page,int?pageSize,[FromServices]PortfolioServiceservice,HttpContexthttp,CancellationTokenct)=>"
        + "Ok(awaitservice.ListAsync(includeArchived??false,page??1,pageSize??50,ct),http));",
        "group.MapGet(\"/{portfolioId}\",async(stringportfolioId,bool?includeArchived,[FromServices]PortfolioServiceservice,HttpContexthttp,CancellationTokenct)=>"
        + "Ok(awaitservice.GetAsync(portfolioId,includeArchived??false,ct),http));",
        "group.MapGet(\"/{portfolioId}/roadmap\",async(stringportfolioId,[FromServices]PortfolioServiceservice,HttpContexthttp,CancellationTokenct)=>"
        + "Ok(awaitservice.GetRoadmapAsync(portfolioId,ct),http));",
        "group.MapPost(\"\",async(SaveGoalRequestrequest,[FromServices]GoalServiceservice,HttpContexthttp,CancellationTokenct)=>"
        + "Ok(awaitservice.SaveAsync(null,request,CorrelationId(http),ct),http));",
        "group.MapPut(\"/{goalId}\",async(stringgoalId,SaveGoalRequestrequest,[FromServices]GoalServiceservice,HttpContexthttp,CancellationTokenct)=>"
        + "Ok(awaitservice.SaveAsync(goalId,request,CorrelationId(http),ct),http));",
        "group.MapPost(\"/{goalId}/key-results\",async(stringgoalId,SaveKeyResultRequestrequest,[FromServices]GoalServiceservice,HttpContexthttp,CancellationTokenct)=>"
        + "Ok(awaitservice.SaveKeyResultAsync(goalId,null,request,CorrelationId(http),ct),http));",
        "group.MapPut(\"/{goalId}/key-results/{keyResultId}\",async(stringgoalId,stringkeyResultId,SaveKeyResultRequestrequest,[FromServices]GoalServiceservice,HttpContexthttp,CancellationTokenct)=>"
        + "Ok(awaitservice.SaveKeyResultAsync(goalId,keyResultId,request,CorrelationId(http),ct),http));",
        "group.MapDelete(\"/{goalId}\",async(stringgoalId,[FromServices]GoalServiceservice,HttpContexthttp,CancellationTokenct)=>"
        + "{awaitservice.ArchiveAsync(goalId,CorrelationId(http),ct);returnOk(new{archived=true},http);});",
        "group.MapPost(\"/{goalId}/key-results/{keyResultId}/progress-updates\",async(stringgoalId,stringkeyResultId,AddKeyResultProgressRequestrequest,[FromServices]GoalServiceservice,HttpContexthttp,CancellationTokenct)=>"
        + "Ok(awaitservice.AddKeyResultProgressAsync(goalId,keyResultId,request,CorrelationId(http),ct),http));",
        "group.MapPost(\"/{goalId}/status-updates\",async(stringgoalId,AddGoalStatusUpdateRequestrequest,[FromServices]GoalServiceservice,HttpContexthttp,CancellationTokenct)=>"
        + "Ok(awaitservice.AddStatusUpdateAsync(goalId,request,CorrelationId(http),ct),http));",
        "group.MapGet(\"\",async(bool?includeArchived,int?page,int?pageSize,[FromServices]GoalServiceservice,HttpContexthttp,CancellationTokenct)=>"
        + "Ok(awaitservice.ListAsync(includeArchived??false,page??1,pageSize??50,ct),http));",
        "group.MapGet(\"/{goalId}\",async(stringgoalId,bool?includeArchived,[FromServices]GoalServiceservice,HttpContexthttp,CancellationTokenct)=>"
        + "Ok(awaitservice.GetAsync(goalId,includeArchived??false,ct),http));",
        "group.MapGet(\"/{goalId}/rollup\",async(stringgoalId,[FromServices]GoalServiceservice,HttpContexthttp,CancellationTokenct)=>"
        + "Ok(awaitservice.GetRollupAsync(goalId,ct),http));",
        "group.MapDelete(\"/{sprintId}/items/{workItemId}\",async(stringsprintId,stringworkItemId,SprintServiceservice,HttpContexthttp,CancellationTokenct)=>"
        + "Ok(awaitservice.UnplanAsync(sprintId,workItemId,CorrelationId(http),ct),http)).WithZumboPermission(PermissionCatalog.WorkItemUpdate);",
        "group.MapGet(\"/projects/{projectId}\",async(stringprojectId,string?after,int?pageSize,SprintServiceservice,HttpContexthttp,CancellationTokenct)=>"
        + "Ok(awaitservice.ListAsync(projectId,after,pageSize??50,ct),http));",
        "group.MapGet(\"/projects/{projectId}/backlog\",async(stringprojectId,string?after,int?pageSize,SprintServiceservice,HttpContexthttp,CancellationTokenct)=>"
        + "Ok(awaitservice.BacklogAsync(projectId,after,pageSize??50,ct),http));",
        "group.MapGet(\"/projects/{projectId}/velocity\",async(stringprojectId,int?sprintCount,SprintServiceservice,HttpContexthttp,CancellationTokenct)=>"
        + "Ok(awaitservice.VelocityAsync(projectId,sprintCount??6,ct),http));",
        "group.MapGet(\"/reports/sprint-burndown/{projectId}/{sprintId}\",async(stringprojectId,stringsprintId,DateOnlystartDate,DateOnlyendDate,SprintServiceservice,HttpContexthttp,CancellationTokenct)=>"
        + "ReportOk(awaitservice.BurndownSnapshotAsync(projectId,sprintId,startDate,endDate,ct),http)).RequireRateLimiting(\"report\");",
        "group.MapGet(\"/reports/sprint-velocity/{projectId}\",async(stringprojectId,int?sprintCount,SprintServiceservice,HttpContexthttp,CancellationTokenct)=>"
        + "ReportOk(awaitservice.VelocitySnapshotAsync(projectId,sprintCount??6,ct),http)).RequireRateLimiting(\"report\");",
        "group.MapGet(\"/{projectId}\",async(stringprojectId,WorkItemTypeSchemaServiceservice,HttpContexthttp,CancellationTokenct)=>"
        + "Ok(awaitservice.GetAsync(projectId,ct),http));",
        "group.MapGet(\"/{projectId}/reports/custom-fields/{fieldKey}\",async(stringprojectId,stringfieldKey,WorkItemTypeSchemaServiceservice,HttpContexthttp,CancellationTokenct)=>"
        + "Ok(awaitservice.GetCustomFieldDistributionAsync(projectId,fieldKey,ct),http));",
        "group.MapGet(\"/{projectId}/reports/issue-types\",async(stringprojectId,WorkItemTypeSchemaServiceservice,HttpContexthttp,CancellationTokenct)=>"
        + "Ok(awaitservice.GetIssueTypeDistributionAsync(projectId,ct),http));",
        "group.MapGet(\"/{sprintId}\",async(stringsprintId,SprintServiceservice,HttpContexthttp,CancellationTokenct)=>"
        + "Ok(awaitservice.GetAsync(sprintId,ct),http));",
        "group.MapGet(\"/{sprintId}/burndown\",async(stringsprintId,DateOnly?startDate,DateOnly?endDate,SprintServiceservice,HttpContexthttp,CancellationTokenct)=>"
        + "{varsprint=awaitservice.GetAsync(sprintId,ct);returnOk(awaitservice.BurndownAsync(sprint.ProjectId,sprintId,startDate,endDate,ct),http);});",
        "group.MapPost(\"\",async(CreateSprintRequestrequest,SprintServiceservice,HttpContexthttp,CancellationTokenct)=>"
        + "Created(awaitservice.CreateAsync(request,CorrelationId(http),ct),http)).WithZumboPermission(PermissionCatalog.WorkItemUpdate);",
        "group.MapPost(\"/{sprintId}/complete\",async(stringsprintId,CompleteSprintRequestrequest,SprintServiceservice,HttpContexthttp,CancellationTokenct)=>"
        + "Ok(awaitservice.CompleteAsync(sprintId,request,CorrelationId(http),ct),http)).WithZumboPermission(PermissionCatalog.WorkItemUpdate);",
        "group.MapPost(\"/{sprintId}/start\",async(stringsprintId,SprintServiceservice,HttpContexthttp,CancellationTokenct)=>"
        + "Ok(awaitservice.StartAsync(sprintId,CorrelationId(http),ct),http)).WithZumboPermission(PermissionCatalog.WorkItemUpdate);",
        "group.MapPut(\"/{projectId}\",async(stringprojectId,UpsertWorkItemTypeSchemaRequestrequest,WorkItemTypeSchemaServiceservice,HttpContexthttp,CancellationTokenct)=>"
        + "Ok(awaitservice.UpsertAsync(projectId,request,CorrelationId(http),ct),http)).WithZumboPermission(PermissionCatalog.WorkItemUpdate);",
        "group.MapPut(\"/{sprintId}/items/{workItemId}\",async(stringsprintId,stringworkItemId,PlanSprintWorkItemRequestrequest,SprintServiceservice,HttpContexthttp,CancellationTokenct)=>"
        + "Ok(awaitservice.PlanAsync(sprintId,workItemId,request,CorrelationId(http),ct),http)).WithZumboPermission(PermissionCatalog.WorkItemUpdate);",
        "group.MapGet(\"/templates\",async(stringprojectId,int?page,int?pageSize,bool?includeArchived,WorkItemTemplateRecurrenceServiceservice,HttpContexthttp,CancellationTokenct)=>"
        + "Ok(awaitservice.ListTemplatesAsync(projectId,page??1,pageSize??50,includeArchived??false,ct),http));",
        "group.MapGet(\"/recurrences\",async(stringprojectId,int?page,int?pageSize,bool?includeArchived,WorkItemTemplateRecurrenceServiceservice,HttpContexthttp,CancellationTokenct)=>"
        + "Ok(awaitservice.ListRecurrencesAsync(projectId,page??1,pageSize??50,includeArchived??false,ct),http));",
        "group.MapGet(\"/recurrences/{recurrenceId}/occurrences\",async(stringrecurrenceId,int?page,int?pageSize,WorkItemTemplateRecurrenceServiceservice,HttpContexthttp,CancellationTokenct)=>"
        + "Ok(awaitservice.ListOccurrencesAsync(recurrenceId,page??1,pageSize??50,ct),http));",
        "group.MapPost(\"/recurrences/preview\",async(PreviewWorkItemRecurrenceRequestrequest,WorkItemTemplateRecurrenceServiceservice,HttpContexthttp,CancellationTokenct)=>"
        + "Ok(awaitservice.PreviewRecurrenceAsync(request,ct),http)).WithZumboPermission(PermissionCatalog.WorkItemCreate);",
        "group.MapPost(\"/recurrences\",async(CreateWorkItemRecurrenceRequestrequest,WorkItemTemplateRecurrenceServiceservice,HttpContexthttp,CancellationTokenct)=>"
        + "Created(awaitservice.CreateRecurrenceAsync(request,CorrelationId(http),ct),http)).WithZumboPermission(PermissionCatalog.WorkItemCreate);",
        "group.MapPatch(\"/recurrences/{recurrenceId}/state\",async(stringrecurrenceId,SetWorkItemRecurrenceStateRequestrequest,WorkItemTemplateRecurrenceServiceservice,HttpContexthttp,CancellationTokenct)=>"
        + "Ok(awaitservice.SetRecurrenceStateAsync(recurrenceId,request.Active,CorrelationId(http),ct),http)).WithZumboPermission(PermissionCatalog.WorkItemUpdate);",
        "group.MapDelete(\"/recurrences/{recurrenceId}\",async(stringrecurrenceId,WorkItemTemplateRecurrenceServiceservice,HttpContexthttp,CancellationTokenct)=>"
        + "{awaitservice.ArchiveRecurrenceAsync(recurrenceId,CorrelationId(http),ct);returnResults.NoContent();}).WithZumboPermission(PermissionCatalog.WorkItemUpdate);",
        "group.MapPost(\"/templates\",async(CreateWorkItemTemplateRequestrequest,WorkItemTemplateRecurrenceServiceservice,HttpContexthttp,CancellationTokenct)=>"
        + "Created(awaitservice.CreateTemplateAsync(request,CorrelationId(http),ct),http)).WithZumboPermission(PermissionCatalog.WorkItemCreate);",
        "group.MapPut(\"/templates/{templateId}\",async(stringtemplateId,UpdateWorkItemTemplateRequestrequest,WorkItemTemplateRecurrenceServiceservice,HttpContexthttp,CancellationTokenct)=>"
        + "Ok(awaitservice.UpdateTemplateAsync(templateId,request,CorrelationId(http),ct),http)).WithZumboPermission(PermissionCatalog.WorkItemUpdate);",
        "group.MapDelete(\"/templates/{templateId}\",async(stringtemplateId,WorkItemTemplateRecurrenceServiceservice,HttpContexthttp,CancellationTokenct)=>"
        + "{awaitservice.ArchiveTemplateAsync(templateId,CorrelationId(http),ct);returnResults.NoContent();}).WithZumboPermission(PermissionCatalog.WorkItemUpdate);",
        "group.MapPost(\"/recurrences/process-due\",async(WorkItemTemplateRecurrenceServiceservice,CancellationTokenct)=>"
        + "Results.Ok(new{scheduled=awaitservice.ScheduleDueAsync(ct)})).WithZumboPermission(PermissionCatalog.OperationsManage,isGlobal:true);",
        "group.MapGet(\"/{documentId}\",async(stringdocumentId,bool?includeArchived,[FromServices]KnowledgeServiceservice,HttpContexthttp,CancellationTokenct)=>"
        + "Ok(awaitservice.GetAsync(documentId,includeArchived??false,ct),http));",
        "group.MapGet(\"\",async(string?query,string?scopeType,string?scopeId,bool?includeArchived,int?page,int?pageSize,[FromServices]KnowledgeServiceservice,HttpContexthttp,CancellationTokenct)=>"
        + "Ok(awaitservice.SearchAsync(query,scopeType,scopeId,includeArchived??false,page??1,pageSize??50,ct),http)).RequireRateLimiting(\"search\");",
        "group.MapGet(\"/scope-link-options\",async(stringscopeType,stringscopeId,string?query,[FromServices]KnowledgeServiceservice,HttpContexthttp,CancellationTokenct)=>"
        + "Ok(awaitservice.GetLinkOptionsAsync(scopeType,scopeId,query,ct),http)).RequireRateLimiting(\"search\");",
        "group.MapGet(\"/{documentId}/versions/{number:int}\",async(stringdocumentId,intnumber,[FromServices]KnowledgeServiceservice,HttpContexthttp,CancellationTokenct)=>"
        + "Ok(awaitservice.GetVersionAsync(documentId,number,ct),http));",
        "group.MapPost(\"/{documentId}/comments\",async(stringdocumentId,AddKnowledgeCommentRequestrequest,[FromServices]KnowledgeServiceservice,HttpContexthttp,CancellationTokenct)=>"
        + "Ok(awaitservice.AddCommentAsync(documentId,request,CorrelationId(http),ct),http));",
        "group.MapPatch(\"/{documentId}/comments/{commentId}/resolve\",async(stringdocumentId,stringcommentId,[FromServices]KnowledgeServiceservice,HttpContexthttp,CancellationTokenct)=>"
        + "Ok(awaitservice.ResolveCommentAsync(documentId,commentId,CorrelationId(http),ct),http));",
        "group.MapPost(\"\",async(CreateKnowledgeDocumentRequestrequest,[FromServices]KnowledgeServiceservice,HttpContexthttp,CancellationTokenct)=>"
        + "Ok(awaitservice.CreateAsync(request,CorrelationId(http),ct),http));",
        "group.MapPut(\"/{documentId}\",async(stringdocumentId,CreateKnowledgeVersionRequestrequest,[FromServices]KnowledgeServiceservice,HttpContexthttp,CancellationTokenct)=>"
        + "Ok(awaitservice.AddVersionAsync(documentId,request,CorrelationId(http),ct),http));",
        "group.MapDelete(\"/{documentId}\",async(stringdocumentId,[FromServices]KnowledgeServiceservice,HttpContexthttp,CancellationTokenct)=>"
        + "{awaitservice.ArchiveAsync(documentId,CorrelationId(http),ct);returnOk(new{archived=true},http);});",
        "group.MapPost(\"/login\",async(LoginRequestrequest,IdentityServiceservice,HttpContexthttp,CancellationTokenct)=>"
        + "Ok(awaitservice.LoginAsync(request,ct),http)).RequireRateLimiting(\"login\");",
        "group.MapPost(\"/logout\",async(LogoutRequestrequest,IdentityServiceservice,HttpContexthttp,CancellationTokenct)=>"
        + "Ok(awaitservice.LogoutAsync(request,ct),http));",
        "group.MapPost(\"/refresh\",async(RefreshTokenRequestrequest,IdentityServiceservice,HttpContexthttp,CancellationTokenct)=>"
        + "Ok(awaitservice.RefreshAsync(request,ct),http));",
        "group.MapPost(\"/change-password\",async(ChangePasswordRequestrequest,IdentityServiceservice,HttpContexthttp,CancellationTokenct)=>"
        + "Ok(awaitservice.ChangePasswordAsync(request,CorrelationId(http),ct),http))"
        + ".RequireAuthorization().WithZumboPermission(PermissionCatalog.ProfileRead);",
        "group.MapPost(\"/forgot-password\",async(ForgotPasswordRequestrequest,IdentityServiceservice,HttpContexthttp,CancellationTokenct)=>"
        + "Ok(awaitservice.ForgotPasswordAsync(request,ct),http)).RequireRateLimiting(\"password-reset\");",
        "group.MapPost(\"/reset-password\",async(ResetPasswordRequestrequest,IdentityServiceservice,HttpContexthttp,CancellationTokenct)=>"
        + "Ok(awaitservice.ResetPasswordAsync(request,CorrelationId(http),ct),http)).RequireRateLimiting(\"password-reset\");",
        "group.MapGet(\"/sessions\",async(IdentityServiceservice,HttpContexthttp,CancellationTokenct)=>"
        + "Ok(awaitservice.ListSessionsAsync(http.User.FindFirst(\"sessionId\")?.Value,ct),http))"
        + ".RequireAuthorization().WithZumboPermission(PermissionCatalog.ProfileRead);",
        "group.MapDelete(\"/sessions/{sessionId}\",async(stringsessionId,IdentityServiceservice,HttpContexthttp,CancellationTokenct)=>"
        + "{awaitservice.RevokeSessionAsync(sessionId,CorrelationId(http),ct);returnOk(new{revoked=true},http);})"
        + ".RequireAuthorization().WithZumboPermission(PermissionCatalog.ProfileRead);",
        "group.MapGet(\"/mfa\",async(IdentityServiceservice,HttpContexthttp,CancellationTokenct)=>"
        + "Ok(awaitservice.GetMfaStatusAsync(ct),http)).RequireAuthorization()"
        + ".WithZumboPermission(PermissionCatalog.ProfileRead);",
        "group.MapPost(\"/mfa/setup\",async(BeginMfaSetupRequestrequest,IdentityServiceservice,HttpContexthttp,CancellationTokenct)=>"
        + "Ok(awaitservice.BeginMfaSetupAsync(request,CorrelationId(http),ct),http)).RequireAuthorization()"
        + ".WithZumboPermission(PermissionCatalog.ProfileRead);",
        "group.MapPost(\"/mfa/confirm\",async(ConfirmMfaSetupRequestrequest,IdentityServiceservice,HttpContexthttp,CancellationTokenct)=>"
        + "Ok(awaitservice.ConfirmMfaSetupAsync(request,CorrelationId(http),ct),http)).RequireAuthorization()"
        + ".WithZumboPermission(PermissionCatalog.ProfileRead);",
        "group.MapPost(\"/mfa/disable\",async(DisableMfaRequestrequest,IdentityServiceservice,HttpContexthttp,CancellationTokenct)=>"
        + "Ok(awaitservice.DisableMfaAsync(request,CorrelationId(http),ct),http)).RequireAuthorization()"
        + ".WithZumboPermission(PermissionCatalog.ProfileRead);",
        "group.MapPost(\"/mfa/recovery-codes\",async(RegenerateMfaRecoveryCodesRequestrequest,IdentityServiceservice,HttpContexthttp,CancellationTokenct)=>"
        + "Ok(awaitservice.RegenerateMfaRecoveryCodesAsync(request,CorrelationId(http),ct),http)).RequireAuthorization()"
        + ".WithZumboPermission(PermissionCatalog.ProfileRead);",
        "group.MapPost(\"/deactivate\",async(DeactivateAccountRequestrequest,IdentityServiceservice,HttpContexthttp,CancellationTokenct)=>"
        + "Ok(awaitservice.DeactivateAsync(request,ct),http)).RequireAuthorization()"
        + ".WithZumboPermission(PermissionCatalog.ProfileRead);",
        "group.MapGet(\"/runs/{runId}\",async(stringrunId,AutomationExecutionServiceservice,HttpContexthttp,CancellationTokenct)=>"
        + "Ok(awaitservice.GetAsync(runId,ct),http));",
        "group.MapGet(\"/runs\",async(stringprojectId,string?ruleId,string?status,int?page,int?pageSize,AutomationExecutionServiceservice,HttpContexthttp,CancellationTokenct)=>"
        + "Ok(awaitservice.ListAsync(projectId,ruleId,status,page??1,pageSize??50,ct),http));",
        "group.MapPost(\"/runs/{runId}/replay\",async(stringrunId,AutomationExecutionServiceservice,HttpContexthttp,CancellationTokenct)=>"
        + "Ok(awaitservice.ReplayAsync(runId,CorrelationId(http),ct),http))"
        + ".WithZumboPermission(PermissionCatalog.WorkflowManage);",
        "group.MapPut(\"/{boardId}\",async(stringboardId,UpdateBoardRequestrequest,BoardServiceservice,HttpContexthttp,CancellationTokenct)=>"
        + "Ok(awaitservice.UpdateAsync(boardId,request,CorrelationId(http),ct),http)).WithZumboPermission(PermissionCatalog.BoardManage);",
        "group.MapDelete(\"/{boardId}\",async(stringboardId,BoardServiceservice,HttpContexthttp,CancellationTokenct)=>"
        + "{awaitservice.ArchiveAsync(boardId,CorrelationId(http),ct);returnOk(new{archived=true},http);})"
        + ".WithZumboPermission(PermissionCatalog.BoardManage);",
        "group.MapPost(\"/{boardId}/restore\",async(stringboardId,BoardServiceservice,HttpContexthttp,CancellationTokenct)=>"
        + "Ok(awaitservice.RestoreAsync(boardId,CorrelationId(http),ct),http))"
        + ".WithZumboPermission(PermissionCatalog.BoardManage);",
        "group.MapPatch(\"/{boardId}/swimlane\",async(stringboardId,UpdateSwimlaneRequestrequest,BoardServiceservice,HttpContexthttp,CancellationTokenct)=>"
        + "Ok(awaitservice.UpdateSwimlaneAsync(boardId,request,CorrelationId(http),ct),http))"
        + ".WithZumboPermission(PermissionCatalog.BoardManage);",
        "group.MapPost(\"/{boardId}/columns\",async(stringboardId,CreateColumnRequestrequest,BoardServiceservice,HttpContexthttp,CancellationTokenct)=>"
        + "Ok(awaitservice.AddColumnAsync(boardId,request,CorrelationId(http),ct),http))"
        + ".WithZumboPermission(PermissionCatalog.BoardManage);",
        "group.MapPut(\"/{boardId}/columns/{columnId}\",async(stringboardId,stringcolumnId,UpdateColumnRequestrequest,BoardServiceservice,HttpContexthttp,CancellationTokenct)=>"
        + "Ok(awaitservice.UpdateColumnAsync(boardId,columnId,request,CorrelationId(http),ct),http))"
        + ".WithZumboPermission(PermissionCatalog.BoardManage);",
        "group.MapDelete(\"/{boardId}/columns/{columnId}\",async(stringboardId,stringcolumnId,BoardServiceservice,HttpContexthttp,CancellationTokenct)=>"
        + "Ok(awaitservice.DeleteColumnAsync(boardId,columnId,CorrelationId(http),ct),http))"
        + ".WithZumboPermission(PermissionCatalog.BoardManage);",
        "group.MapPut(\"/{boardId}/columns/reorder\",async(stringboardId,ReorderColumnsRequestrequest,BoardServiceservice,HttpContexthttp,CancellationTokenct)=>"
        + "Ok(awaitservice.ReorderColumnsAsync(boardId,request,CorrelationId(http),ct),http))"
        + ".WithZumboPermission(PermissionCatalog.BoardManage);",
        "group.MapPost(\"/{boardId}/views\",async(stringboardId,CreateBoardViewRequestrequest,BoardServiceservice,HttpContexthttp,CancellationTokenct)=>"
        + "Ok(awaitservice.CreateViewAsync(boardId,request,CorrelationId(http),ct),http))"
        + ".WithZumboPermission(PermissionCatalog.BoardManage);",
        "group.MapPut(\"/{boardId}/views/{viewId}\",async(stringboardId,stringviewId,UpdateBoardViewRequestrequest,BoardServiceservice,HttpContexthttp,CancellationTokenct)=>"
        + "Ok(awaitservice.UpdateViewAsync(boardId,viewId,request,CorrelationId(http),ct),http))"
        + ".WithZumboPermission(PermissionCatalog.BoardManage);",
        "group.MapDelete(\"/{boardId}/views/{viewId}\",async(stringboardId,stringviewId,BoardServiceservice,HttpContexthttp,CancellationTokenct)=>"
        + "Ok(awaitservice.DeleteViewAsync(boardId,viewId,CorrelationId(http),ct),http))"
        + ".WithZumboPermission(PermissionCatalog.BoardManage);",
        "group.MapDelete(\"/{planId}\",async(stringplanId,[FromServices]CapacityPlanningServiceservice,HttpContexthttp,CancellationTokenct)=>"
        + "{awaitservice.ArchiveAsync(planId,CorrelationId(http),ct);returnOk(new{archived=true},http);});",
        "group.MapGet(\"/{planId}\",async(stringplanId,bool?includeArchived,[FromServices]CapacityPlanningServiceservice,HttpContexthttp,CancellationTokenct)=>"
        + "Ok(awaitservice.GetAsync(planId,includeArchived??false,ct),http));",
        "group.MapGet(\"\",async(bool?includeArchived,int?page,int?pageSize,[FromServices]CapacityPlanningServiceservice,HttpContexthttp,CancellationTokenct)=>"
        + "Ok(awaitservice.ListAsync(includeArchived??false,page??1,pageSize??50,ct),http));",
        "group.MapPut(\"/{planId}/sharing\",async(stringplanId,ShareCapacityPlanRequestrequest,[FromServices]CapacityPlanningServiceservice,HttpContexthttp,CancellationTokenct)=>"
        + "Ok(awaitservice.ShareAsync(planId,request,CorrelationId(http),ct),http));",
        "group.MapPost(\"\",async(SaveCapacityPlanRequestrequest,[FromServices]CapacityPlanningServiceservice,HttpContexthttp,CancellationTokenct)=>"
        + "Ok(awaitservice.SaveAsync(null,request,CorrelationId(http),ct),http));",
        "group.MapPut(\"/{planId}\",async(stringplanId,SaveCapacityPlanRequestrequest,[FromServices]CapacityPlanningServiceservice,HttpContexthttp,CancellationTokenct)=>"
        + "Ok(awaitservice.SaveAsync(planId,request,CorrelationId(http),ct),http));",
        "group.MapGet(\"/{planId}/snapshot\",async(stringplanId,[FromServices]CapacityPlanningServiceservice,HttpContexthttp,CancellationTokenct)=>"
        + "Ok(awaitservice.GetSnapshotAsync(planId,ct),http)).RequireRateLimiting(\"report\");",
        "group.MapPost(\"/{planId}/scenarios\",async(stringplanId,CapacityScenarioRequestrequest,[FromServices]CapacityPlanningServiceservice,HttpContexthttp,CancellationTokenct)=>"
        + "Ok(awaitservice.PreviewScenarioAsync(planId,request,ct),http)).RequireRateLimiting(\"report\");",
        "group.MapGet(\"/\",async(WorkItemWebhookServiceservice,HttpContexthttp,CancellationTokenct)=>"
        + "Ok(awaitservice.ListAsync(ct),http));",
        "group.MapGet(\"/{id}\",async(stringid,WorkItemWebhookServiceservice,HttpContexthttp,CancellationTokenct)=>"
        + "Ok(awaitservice.GetAsync(id,ct),http));",
        "group.MapGet(\"/{id}\",async(stringid,WorkItemServiceservice,HttpContexthttp,CancellationTokenct)=>"
        + "Ok(awaitservice.GetAsync(id,ct),http));",
        "group.MapPost(\"/search\",async(WorkItemSearchRequestrequest,WorkItemServiceservice,HttpContexthttp,CancellationTokenct)=>"
        + "Ok(awaitservice.SearchPageAsync(request,ct),http)).RequireRateLimiting(\"search\");",
        "group.MapPost(\"/bulk/move\",async(BulkMoveWorkItemsRequestrequest,WorkItemServiceservice,HttpContexthttp,CancellationTokenct)=>"
        + "Ok(awaitservice.BulkMoveAsync(request,CorrelationId(http),ct),http)).WithZumboPermission(PermissionCatalog.WorkItemMove).RequireRateLimiting(\"bulk\");",
        "group.MapPost(\"/bulk/assign\",async(BulkAssignWorkItemsRequestrequest,WorkItemServiceservice,HttpContexthttp,CancellationTokenct)=>"
        + "Ok(awaitservice.BulkAssignAsync(request,CorrelationId(http),ct),http)).WithZumboPermission(PermissionCatalog.WorkItemAssign).RequireRateLimiting(\"bulk\");",
        "group.MapPost(\"/bulk/archive\",async(BulkArchiveWorkItemsRequestrequest,WorkItemServiceservice,HttpContexthttp,CancellationTokenct)=>"
        + "Ok(awaitservice.BulkArchiveAsync(request,CorrelationId(http),ct),http)).WithZumboPermission(PermissionCatalog.WorkItemDelete).RequireRateLimiting(\"bulk\");",
        "group.MapPut(\"/{id}\",async(stringid,UpdateWorkItemRequestrequest,WorkItemServiceservice,HttpContexthttp,CancellationTokenct)=>"
        + "Ok(awaitservice.UpdateAsync(id,request,CorrelationId(http),ct),http))"
        + ".WithZumboPermission(PermissionCatalog.WorkItemUpdate);",
        "group.MapDelete(\"/{id}\",async(stringid,WorkItemServiceservice,HttpContexthttp,CancellationTokenct)=>"
        + "{awaitservice.ArchiveAsync(id,CorrelationId(http),ct);returnOk(new{archived=true},http);})"
        + ".WithZumboPermission(PermissionCatalog.WorkItemDelete);",
        "group.MapPost(\"/{id}/restore\",async(stringid,WorkItemServiceservice,HttpContexthttp,CancellationTokenct)=>"
        + "Ok(awaitservice.RestoreAsync(id,CorrelationId(http),ct),http))"
        + ".WithZumboPermission(PermissionCatalog.WorkItemDelete);",
        "group.MapPost(\"/{id}/labels\",async(stringid,AddLabelRequestrequest,WorkItemServiceservice,HttpContexthttp,CancellationTokenct)=>"
        + "Ok(awaitservice.AddLabelAsync(id,request,ct),http))"
        + ".WithZumboPermission(PermissionCatalog.WorkItemUpdate);",
        "group.MapDelete(\"/{id}/labels/{label}\",async(stringid,stringlabel,WorkItemServiceservice,HttpContexthttp,CancellationTokenct)=>"
        + "Ok(awaitservice.RemoveLabelAsync(id,label,ct),http))"
        + ".WithZumboPermission(PermissionCatalog.WorkItemUpdate);",
        "group.MapPost(\"/{id}/comments\",async(stringid,AddCommentRequestrequest,WorkItemServiceservice,HttpContexthttp,CancellationTokenct)=>"
        + "Ok(awaitservice.AddCommentAsync(id,request,CorrelationId(http),ct),http))"
        + ".WithZumboPermission(PermissionCatalog.CommentCreate);",
        "group.MapPut(\"/{id}/comments/{commentId}\",async(stringid,stringcommentId,EditCommentRequestrequest,WorkItemServiceservice,HttpContexthttp,CancellationTokenct)=>"
        + "Ok(awaitservice.EditCommentAsync(id,commentId,request,CorrelationId(http),ct),http))"
        + ".WithZumboPermission(PermissionCatalog.CommentCreate);",
        "group.MapDelete(\"/{id}/comments/{commentId}\",async(stringid,stringcommentId,WorkItemServiceservice,HttpContexthttp,CancellationTokenct)=>"
        + "Ok(awaitservice.DeleteCommentAsync(id,commentId,CorrelationId(http),ct),http))"
        + ".WithZumboPermission(PermissionCatalog.CommentCreate);",
        "group.MapPost(\"/{id}/checklist\",async(stringid,AddChecklistItemRequestrequest,WorkItemServiceservice,HttpContexthttp,CancellationTokenct)=>"
        + "Ok(awaitservice.AddChecklistItemAsync(id,request,ct),http))"
        + ".WithZumboPermission(PermissionCatalog.WorkItemUpdate);",
        "group.MapPatch(\"/{id}/checklist/{itemId}\",async(stringid,stringitemId,CompleteChecklistItemRequestrequest,WorkItemServiceservice,HttpContexthttp,CancellationTokenct)=>"
        + "Ok(awaitservice.CompleteChecklistItemAsync(id,itemId,request,ct),http))"
        + ".WithZumboPermission(PermissionCatalog.WorkItemUpdate);",
        "group.MapPost(\"/{id}/worklogs\",async(stringid,AddWorkLogRequestrequest,WorkItemServiceservice,HttpContexthttp,CancellationTokenct)=>"
        + "Ok(awaitservice.AddWorkLogAsync(id,request,ct),http))"
        + ".WithZumboPermission(PermissionCatalog.WorkLogCreate);",
        "group.MapPatch(\"/{id}/planning\",async(stringid,SetWorkItemPlanningRequestrequest,WorkItemServiceservice,HttpContexthttp,CancellationTokenct)=>"
        + "Ok(awaitservice.SetPlanningAsync(id,request,ct),http))"
        + ".WithZumboPermission(PermissionCatalog.WorkItemUpdate);",
        "group.MapPatch(\"/{id}/status\",async(stringid,MoveWorkItemRequestrequest,WorkItemServiceservice,HttpContexthttp,CancellationTokenct)=>"
        + "Ok(awaitservice.MoveAsync(id,request,CorrelationId(http),ct),http))"
        + ".WithZumboPermission(PermissionCatalog.WorkItemMove);",
        "group.MapPatch(\"/{id}/rank\",async(stringid,ReorderWorkItemRequestrequest,WorkItemServiceservice,HttpContexthttp,CancellationTokenct)=>"
        + "Ok(awaitservice.ReorderAsync(id,request,CorrelationId(http),ct),http))"
        + ".WithZumboPermission(PermissionCatalog.WorkItemMove);",
        "group.MapPatch(\"/{id}/assignee\",async(stringid,AssignWorkItemRequestrequest,WorkItemServiceservice,HttpContexthttp,CancellationTokenct)=>"
        + "Ok(awaitservice.AssignAsync(id,request,CorrelationId(http),ct),http))"
        + ".WithZumboPermission(PermissionCatalog.WorkItemAssign);",
        "group.MapPatch(\"/{id}/team\",async(stringid,SetWorkItemTeamRequestrequest,WorkItemServiceservice,HttpContexthttp,CancellationTokenct)=>"
        + "Ok(awaitservice.SetTeamAsync(id,request,CorrelationId(http),ct),http))"
        + ".WithZumboPermission(PermissionCatalog.WorkItemUpdate);",
        "group.MapPut(\"/{id}/custom-fields\",async(stringid,SetWorkItemCustomFieldsRequestrequest,WorkItemServiceservice,HttpContexthttp,CancellationTokenct)=>"
        + "Ok(awaitservice.SetCustomFieldsAsync(id,request,CorrelationId(http),ct),http))"
        + ".WithZumboPermission(PermissionCatalog.WorkItemUpdate);",
        "group.MapPost(\"/{id}/approvals\",async(stringid,RequestWorkItemApprovalRequestrequest,WorkItemServiceservice,HttpContexthttp,CancellationTokenct)=>"
        + "Ok(awaitservice.RequestApprovalAsync(id,request,CorrelationId(http),ct),http))"
        + ".WithZumboPermission(PermissionCatalog.WorkItemApprove);",
        "group.MapPost(\"/{id}/approvals/{approvalId}/decision\",async(stringid,stringapprovalId,DecideWorkItemApprovalRequestrequest,WorkItemServiceservice,HttpContexthttp,CancellationTokenct)=>"
        + "Ok(awaitservice.DecideApprovalAsync(id,approvalId,request,CorrelationId(http),ct),http))"
        + ".WithZumboPermission(PermissionCatalog.WorkItemApprove);",
        "group.MapGet(\"/metrics\",async(WorkItemWebhookServiceservice,HttpContexthttp,CancellationTokenct)=>"
        + "Ok(awaitservice.GetMetricsAsync(ct),http));",
        "group.MapGet(\"/{id}/deliveries\",async(stringid,string?cursor,int?pageSize,WorkItemWebhookServiceservice,HttpContexthttp,CancellationTokenct)=>"
        + "Ok(awaitservice.ListDeliveriesAsync(id,cursor,pageSize??50,ct),http));",
        "group.MapGet(\"/deliveries/{deliveryId}\",async(stringdeliveryId,WorkItemWebhookServiceservice,HttpContexthttp,CancellationTokenct)=>"
        + "Ok(awaitservice.GetDeliveryAsync(deliveryId,ct),http));",
        "group.MapPost(\"/deliveries/{deliveryId}/replay\",async(stringdeliveryId,WorkItemWebhookServiceservice,HttpContexthttp,CancellationTokenct)=>"
        + "Ok(awaitservice.ReplayAsync(deliveryId,ct,http.TraceIdentifier),http)).RequireRateLimiting(\"bulk\");",
        "group.MapPost(\"/{id}/enable\",async(stringid,SetWebhookSubscriptionStateRequestrequest,WorkItemWebhookServiceservice,HttpContexthttp,CancellationTokenct)=>"
        + "Ok(awaitservice.SetActiveAsync(id,true,request,ct,http.TraceIdentifier),http));",
        "group.MapPost(\"/{id}/disable\",async(stringid,SetWebhookSubscriptionStateRequestrequest,WorkItemWebhookServiceservice,HttpContexthttp,CancellationTokenct)=>"
        + "Ok(awaitservice.SetActiveAsync(id,false,request,ct,http.TraceIdentifier),http));",
        "group.MapPut(\"/{id}\",async(stringid,UpdateWebhookSubscriptionRequestrequest,WorkItemWebhookServiceservice,HttpContexthttp,CancellationTokenct)=>"
        + "Ok(awaitservice.UpdateAsync(id,request,ct,http.TraceIdentifier),http));",
        "group.MapPost(\"/\",async(CreateWebhookSubscriptionRequestrequest,WorkItemWebhookServiceservice,HttpContexthttp,CancellationTokenct)=>"
        + "Created(awaitservice.CreateAsync(request,ct,http.TraceIdentifier),http));",
        "group.MapPost(\"/{id}/rotate-secret\",async(stringid,RotateWebhookSecretRequestrequest,WorkItemWebhookServiceservice,HttpContexthttp,CancellationTokenct)=>"
        + "Ok(awaitservice.RotateSecretAsync(id,request,ct,http.TraceIdentifier),http)).RequireRateLimiting(\"bulk\");",
        "group.MapPost(\"/{id}/test-delivery\",async(stringid,WorkItemWebhookServiceservice,HttpContexthttp,CancellationTokenct)=>"
        + "Created(awaitservice.QueueTestDeliveryAsync(id,ct,http.TraceIdentifier),http)).RequireRateLimiting(\"bulk\");",
        "management.MapPost(\"/{connectionId}/health\",async(stringconnectionId,DevelopmentIntegrationServiceservice,HttpContexthttp,CancellationTokenct)=>"
        + "Ok(awaitservice.CheckHealthAsync(connectionId,CorrelationId(http),ct),http)).RequireRateLimiting(\"bulk\");",
        "management.MapGet(\"/{connectionId}/repositories\",async(stringconnectionId,DevelopmentIntegrationServiceservice,HttpContexthttp,CancellationTokenct)=>"
        + "{varresult=awaitservice.ListProviderRepositoriesAsync(connectionId,ct);returnOk(newDevelopmentRepositoryPage("
        + "result.Items.Select(item=>newDevelopmentRepositoryResponse(item.ExternalRepositoryId,item.Name,item.FullName,item.Url,item.DefaultBranch))"
        + ".ToList(),result.Partial?\"Partial\":\"Complete\"),http);}).RequireRateLimiting(\"bulk\");",
        "management.MapGet(\"/\",async(DevelopmentIntegrationServiceservice,HttpContexthttp,CancellationTokenct)=>"
        + "Ok(awaitservice.ListAsync(ct),http));",
        "management.MapGet(\"/{connectionId}\",async(stringconnectionId,DevelopmentIntegrationServiceservice,HttpContexthttp,CancellationTokenct)=>"
        + "Ok(awaitservice.GetAsync(connectionId,ct),http));",
        "management.MapPost(\"/\",async(CreateDevelopmentConnectionRequestrequest,DevelopmentIntegrationServiceservice,HttpContexthttp,CancellationTokenct)=>"
        + "Created(awaitservice.CreateAsync(request,CorrelationId(http),ct),http));",
        "management.MapPost(\"/{connectionId}/rotate-credential\",async(stringconnectionId,RotateDevelopmentCredentialRequestrequest,DevelopmentIntegrationServiceservice,HttpContexthttp,CancellationTokenct)=>"
        + "Ok(awaitservice.RotateCredentialAsync(connectionId,request,CorrelationId(http),ct),http)).RequireRateLimiting(\"bulk\");",
        "management.MapPost(\"/{connectionId}/rotate-webhook-secret\",async(stringconnectionId,DevelopmentVersionRequestrequest,DevelopmentIntegrationServiceservice,HttpContexthttp,CancellationTokenct)=>"
        + "Ok(awaitservice.RotateWebhookSecretAsync(connectionId,request,CorrelationId(http),ct),http)).RequireRateLimiting(\"bulk\");",
        "management.MapPost(\"/{connectionId}/disconnect\",async(stringconnectionId,DevelopmentVersionRequestrequest,DevelopmentIntegrationServiceservice,HttpContexthttp,CancellationTokenct)=>"
        + "Ok(awaitservice.DisconnectAsync(connectionId,request,CorrelationId(http),ct),http));",
        "management.MapDelete(\"/{connectionId}\",async(stringconnectionId,longexpectedVersion,DevelopmentIntegrationServiceservice,HttpContexthttp,CancellationTokenct)=>"
        + "{awaitservice.DeleteConnectionAsync(connectionId,expectedVersion,CorrelationId(http),ct);returnResults.NoContent();});",
        "management.MapGet(\"/{connectionId}/mappings\",async(stringconnectionId,DevelopmentIntegrationServiceservice,HttpContexthttp,CancellationTokenct)=>"
        + "Ok(awaitservice.ListMappingsAsync(connectionId,ct),http));",
        "management.MapPost(\"/{connectionId}/mappings\",async(stringconnectionId,CreateDevelopmentRepositoryMappingRequestrequest,DevelopmentIntegrationServiceservice,HttpContexthttp,CancellationTokenct)=>"
        + "Created(awaitservice.CreateMappingAsync(connectionId,request,CorrelationId(http),ct),http));",
        "management.MapDelete(\"/mappings/{mappingId}\",async(stringmappingId,longexpectedVersion,DevelopmentIntegrationServiceservice,HttpContexthttp,CancellationTokenct)=>"
        + "{awaitservice.DeleteMappingAsync(mappingId,expectedVersion,CorrelationId(http),ct);returnResults.NoContent();});",
        "workItemLinks.MapGet(\"/mappings\",async(stringworkItemId,DevelopmentIntegrationServiceservice,HttpContexthttp,CancellationTokenct)=>"
        + "Ok(awaitservice.ListWorkItemMappingsAsync(workItemId,ct),http)).WithZumboPermission(PermissionCatalog.WorkItemLink);",
        "workItemLinks.MapGet(\"/\",async(stringworkItemId,DevelopmentIntegrationServiceservice,HttpContexthttp,CancellationTokenct)=>"
        + "Ok(awaitservice.ListWorkItemLinksAsync(workItemId,ct),http));",
        "workItemLinks.MapPost(\"/\",async(stringworkItemId,CreateWorkItemDevelopmentLinkRequestrequest,DevelopmentIntegrationServiceservice,HttpContexthttp,CancellationTokenct)=>"
        + "Created(awaitservice.CreateWorkItemLinkAsync(workItemId,request,CorrelationId(http),ct),http)).WithZumboPermission(PermissionCatalog.WorkItemLink);",
        "workItemLinks.MapDelete(\"/{linkId}\",async(stringworkItemId,stringlinkId,longexpectedVersion,DevelopmentIntegrationServiceservice,HttpContexthttp,CancellationTokenct)=>"
        + "{awaitservice.DeleteWorkItemLinkAsync(workItemId,linkId,expectedVersion,CorrelationId(http),ct);returnResults.NoContent();}).WithZumboPermission(PermissionCatalog.WorkItemLink);",
        "ingress.MapPost(\"/{connectionId}/webhook\",ReceiveWebhookAsync).AllowAnonymous();",
        "group.MapPatch(\"/{id}/parent\",async(stringid,SetWorkItemParentRequestrequest,WorkItemServiceservice,HttpContexthttp,CancellationTokenct)=>"
        + "Ok(awaitservice.SetParentAsync(id,request,CorrelationId(http),ct),http))"
        + ".WithZumboPermission(PermissionCatalog.WorkItemUpdate);",
        "group.MapPost(\"/{id}/relations\",async(stringid,LinkWorkItemRequestrequest,WorkItemServiceservice,HttpContexthttp,CancellationTokenct)=>"
        + "Ok(awaitservice.LinkAsync(id,request,CorrelationId(http),ct),http))"
        + ".WithZumboPermission(PermissionCatalog.WorkItemLink);",
        "group.MapDelete(\"/{id}/relations/{relatedWorkItemId}\",async(stringid,stringrelatedWorkItemId,stringrelationType,WorkItemServiceservice,HttpContexthttp,CancellationTokenct)=>"
        + "Ok(awaitservice.UnlinkAsync(id,relatedWorkItemId,relationType,CorrelationId(http),ct),http))"
        + ".WithZumboPermission(PermissionCatalog.WorkItemLink);",
        "group.MapPost(\"/{id}/attachments/upload\",async(stringid,IFormFilefile,WorkItemServiceservice,HttpContexthttp,CancellationTokenct)=>{awaitusingvarstream=file.OpenReadStream();returnOk(awaitservice.UploadAttachmentAsync(id,stream,file.FileName,file.ContentType,file.Length,CorrelationId(http),ct),http);}).WithZumboPermission(PermissionCatalog.AttachmentCreate).DisableAntiforgery().RequireRateLimiting(\"upload\");",
        "group.MapDelete(\"/{id}/attachments/{attachmentId}\",async(stringid,stringattachmentId,WorkItemServiceservice,HttpContexthttp,CancellationTokenct)=>Ok(awaitservice.DeleteAttachmentAsync(id,attachmentId,CorrelationId(http),ct),http)).WithZumboPermission(PermissionCatalog.AttachmentDelete);",
        "group.MapGet(\"/reports/project-summary/{projectId}\",async(stringprojectId,WorkItemServiceservice,HttpContexthttp,CancellationTokenct)=>ReportOk(awaitservice.ProjectSummarySnapshotAsync(projectId,ct),http)).RequireRateLimiting(\"report\");",
        "group.MapGet(\"/reports/status-distribution/{projectId}\",async(stringprojectId,WorkItemServiceservice,HttpContexthttp,CancellationTokenct)=>ReportOk(awaitservice.StatusDistributionSnapshotAsync(projectId,ct),http)).RequireRateLimiting(\"report\");",
        "group.MapGet(\"/reports/user-workload/{projectId}\",async(stringprojectId,WorkItemServiceservice,HttpContexthttp,CancellationTokenct)=>ReportOk(awaitservice.UserWorkloadSnapshotAsync(projectId,ct),http)).RequireRateLimiting(\"report\");",
        "group.MapGet(\"/reports/due-date-risks/{projectId}\",async(stringprojectId,int?days,WorkItemServiceservice,HttpContexthttp,CancellationTokenct)=>ReportOk(awaitservice.DueDateRisksSnapshotAsync(projectId,days??14,ct),http)).RequireRateLimiting(\"report\");",
        "group.MapGet(\"/reports/flow-time/{projectId}\",async(stringprojectId,DateOnly?from,DateOnly?to,WorkItemServiceservice,HttpContexthttp,CancellationTokenct)=>ReportOk(awaitservice.FlowTimeSnapshotAsync(projectId,from,to,ct),http)).RequireRateLimiting(\"report\");",
        "group.MapGet(\"/reports/completion-rate/{projectId}\",async(stringprojectId,DateOnly?from,DateOnly?to,WorkItemServiceservice,HttpContexthttp,CancellationTokenct)=>ReportOk(awaitservice.CompletionRateSnapshotAsync(projectId,from,to,ct),http)).RequireRateLimiting(\"report\");",
        "group.MapGet(\"/reports/team-performance/{projectId}\",async(stringprojectId,DateOnly?from,DateOnly?to,WorkItemServiceservice,HttpContexthttp,CancellationTokenct)=>ReportOk(awaitservice.TeamPerformanceSnapshotAsync(projectId,from,to,ct),http)).RequireRateLimiting(\"report\");",
        "group.MapGet(\"/{id}/attachments/{attachmentId}/preview\",async(stringid,stringattachmentId,WorkItemServiceservice,HttpContexthttp,CancellationTokenct)=>"
        + "{varattachment=awaitservice.OpenAttachmentAsync(id,attachmentId,ct);"
        + "if(!IsPreviewableContentType(attachment.ContentType)){awaitattachment.Content.DisposeAsync();returnResults.StatusCode(StatusCodes.Status415UnsupportedMediaType);}"
        + "http.Response.Headers.CacheControl=\"private, no-store\";http.Response.Headers.Pragma=\"no-cache\";"
        + "http.Response.Headers.ContentSecurityPolicy=\"sandbox; default-src 'none'\";"
        + "http.Response.Headers[\"Cross-Origin-Resource-Policy\"]=\"same-origin\";"
        + "http.Response.Headers.ContentDisposition=$\"inline; filename*=UTF-8''{Uri.EscapeDataString(attachment.FileName)}\";"
        + "returnResults.Stream(attachment.Content,attachment.ContentType,enableRangeProcessing:true);});",
        "group.MapGet(\"/{id}/attachments/{attachmentId}/download\",async(stringid,stringattachmentId,WorkItemServiceservice,HttpContexthttp,CancellationTokenct)=>"
        + "{varattachment=awaitservice.OpenAttachmentAsync(id,attachmentId,ct);"
        + "http.Response.Headers.CacheControl=\"private, no-store\";http.Response.Headers.Pragma=\"no-cache\";"
        + "returnResults.File(attachment.Content,attachment.ContentType,attachment.FileName,enableRangeProcessing:true);});"
    ];

    private static readonly string[] PortFocusedVerticalSliceEndpointMappings =
    [
        "group.MapPost(\"/{portfolioId}/dependencies\",async(stringportfolioId,SavePortfolioDependencyRequestrequest,[FromServices]SavePortfolioDependencyHandlerhandler,HttpContexthttp,CancellationTokenct)=>"
        + "Ok(awaithandler.HandleAsync(newSavePortfolioDependencyCommand(portfolioId,null,request,CorrelationId(http)),ct),http));",
        "group.MapPut(\"/{portfolioId}/dependencies/{dependencyId}\",async(stringportfolioId,stringdependencyId,SavePortfolioDependencyRequestrequest,[FromServices]SavePortfolioDependencyHandlerhandler,HttpContexthttp,CancellationTokenct)=>"
        + "Ok(awaithandler.HandleAsync(newSavePortfolioDependencyCommand(portfolioId,dependencyId,request,CorrelationId(http)),ct),http));",
        "group.MapPost(\"/{portfolioId}/initiatives\",async(stringportfolioId,SaveInitiativeRequestrequest,[FromServices]SaveInitiativeHandlerhandler,HttpContexthttp,CancellationTokenct)=>"
        + "Ok(awaithandler.HandleAsync(newSaveInitiativeCommand(portfolioId,null,request,CorrelationId(http)),ct),http));",
        "group.MapPut(\"/{portfolioId}/initiatives/{initiativeId}\",async(stringportfolioId,stringinitiativeId,SaveInitiativeRequestrequest,[FromServices]SaveInitiativeHandlerhandler,HttpContexthttp,CancellationTokenct)=>"
        + "Ok(awaithandler.HandleAsync(newSaveInitiativeCommand(portfolioId,initiativeId,request,CorrelationId(http)),ct),http));",
        "group.MapPost(\"/{portfolioId}/initiatives/{initiativeId}/status-updates\",async(stringportfolioId,stringinitiativeId,AddInitiativeStatusUpdateRequestrequest,[FromServices]AddInitiativeStatusUpdateHandlerhandler,HttpContexthttp,CancellationTokenct)=>"
        + "Ok(awaithandler.HandleAsync(newAddInitiativeStatusUpdateCommand(portfolioId,initiativeId,request,CorrelationId(http)),ct),http));",
        "group.MapPost(\"\",async(SavePortfolioRequestrequest,[FromServices]SavePortfolioHandlerhandler,HttpContexthttp,CancellationTokenct)=>"
        + "Ok(awaithandler.HandleAsync(newSavePortfolioCommand(null,request,CorrelationId(http)),ct),http));",
        "group.MapPut(\"/{portfolioId}\",async(stringportfolioId,SavePortfolioRequestrequest,[FromServices]SavePortfolioHandlerhandler,HttpContexthttp,CancellationTokenct)=>"
        + "Ok(awaithandler.HandleAsync(newSavePortfolioCommand(portfolioId,request,CorrelationId(http)),ct),http));",
        "group.MapDelete(\"/{portfolioId}\",async(stringportfolioId,[FromServices]ArchivePortfolioHandlerhandler,HttpContexthttp,CancellationTokenct)=>"
        + "{awaithandler.HandleAsync(newArchivePortfolioCommand(portfolioId,CorrelationId(http)),ct);returnOk(new{archived=true},http);});",
        "group.MapGet(\"\",async(bool?includeArchived,int?page,int?pageSize,[FromServices]ListPortfoliosHandlerhandler,HttpContexthttp,CancellationTokenct)=>"
        + "Ok(awaithandler.HandleAsync(newListPortfoliosQuery(includeArchived??false,page??1,pageSize??50),ct),http));",
        "group.MapGet(\"/{portfolioId}\",async(stringportfolioId,bool?includeArchived,[FromServices]GetPortfolioHandlerhandler,HttpContexthttp,CancellationTokenct)=>"
        + "Ok(awaithandler.HandleAsync(newGetPortfolioQuery(portfolioId,includeArchived??false),ct),http));",
        "group.MapGet(\"/{portfolioId}/roadmap\",async(stringportfolioId,[FromServices]GetPortfolioRoadmapHandlerhandler,HttpContexthttp,CancellationTokenct)=>"
        + "Ok(awaithandler.HandleAsync(newGetPortfolioRoadmapQuery(portfolioId),ct),http));",
        "group.MapPost(\"\",async(SaveGoalRequestrequest,[FromServices]SaveGoalHandlerhandler,HttpContexthttp,CancellationTokenct)=>"
        + "Ok(awaithandler.HandleAsync(newSaveGoalCommand(null,request,CorrelationId(http)),ct),http));",
        "group.MapPut(\"/{goalId}\",async(stringgoalId,SaveGoalRequestrequest,[FromServices]SaveGoalHandlerhandler,HttpContexthttp,CancellationTokenct)=>"
        + "Ok(awaithandler.HandleAsync(newSaveGoalCommand(goalId,request,CorrelationId(http)),ct),http));",
        "group.MapPost(\"/{goalId}/key-results\",async(stringgoalId,SaveKeyResultRequestrequest,[FromServices]SaveKeyResultHandlerhandler,HttpContexthttp,CancellationTokenct)=>"
        + "Ok(awaithandler.HandleAsync(newSaveKeyResultCommand(goalId,null,request,CorrelationId(http)),ct),http));",
        "group.MapPut(\"/{goalId}/key-results/{keyResultId}\",async(stringgoalId,stringkeyResultId,SaveKeyResultRequestrequest,[FromServices]SaveKeyResultHandlerhandler,HttpContexthttp,CancellationTokenct)=>"
        + "Ok(awaithandler.HandleAsync(newSaveKeyResultCommand(goalId,keyResultId,request,CorrelationId(http)),ct),http));",
        "group.MapDelete(\"/{goalId}\",async(stringgoalId,[FromServices]ArchiveGoalHandlerhandler,HttpContexthttp,CancellationTokenct)=>"
        + "{awaithandler.HandleAsync(newArchiveGoalCommand(goalId,CorrelationId(http)),ct);returnOk(new{archived=true},http);});",
        "group.MapPost(\"/{goalId}/key-results/{keyResultId}/progress-updates\",async(stringgoalId,stringkeyResultId,AddKeyResultProgressRequestrequest,[FromServices]AddKeyResultProgressHandlerhandler,HttpContexthttp,CancellationTokenct)=>"
        + "Ok(awaithandler.HandleAsync(newAddKeyResultProgressCommand(goalId,keyResultId,request,CorrelationId(http)),ct),http));",
        "group.MapPost(\"/{goalId}/status-updates\",async(stringgoalId,AddGoalStatusUpdateRequestrequest,[FromServices]AddGoalStatusUpdateHandlerhandler,HttpContexthttp,CancellationTokenct)=>"
        + "Ok(awaithandler.HandleAsync(newAddGoalStatusUpdateCommand(goalId,request,CorrelationId(http)),ct),http));",
        "group.MapGet(\"\",async(bool?includeArchived,int?page,int?pageSize,[FromServices]ListGoalsHandlerhandler,HttpContexthttp,CancellationTokenct)=>"
        + "Ok(awaithandler.HandleAsync(newListGoalsQuery(includeArchived??false,page??1,pageSize??50),ct),http));",
        "group.MapGet(\"/{goalId}\",async(stringgoalId,bool?includeArchived,[FromServices]GetGoalHandlerhandler,HttpContexthttp,CancellationTokenct)=>"
        + "Ok(awaithandler.HandleAsync(newGetGoalQuery(goalId,includeArchived??false),ct),http));",
        "group.MapGet(\"/{goalId}/rollup\",async(stringgoalId,[FromServices]GetGoalRollupHandlerhandler,HttpContexthttp,CancellationTokenct)=>"
        + "Ok(awaithandler.HandleAsync(newGetGoalRollupQuery(goalId),ct),http));",
        "group.MapDelete(\"/{sprintId}/items/{workItemId}\",async(stringsprintId,stringworkItemId,UnplanSprintWorkItemHandlerhandler,HttpContexthttp,CancellationTokenct)=>"
        + "Ok(awaithandler.HandleAsync(newUnplanSprintWorkItemCommand(sprintId,workItemId,CorrelationId(http)),ct),http)).WithZumboPermission(PermissionCatalog.WorkItemUpdate);",
        "group.MapGet(\"/projects/{projectId}\",async(stringprojectId,string?after,int?pageSize,ListSprintsHandlerhandler,HttpContexthttp,CancellationTokenct)=>"
        + "Ok(awaithandler.HandleAsync(newListSprintsQuery(projectId,after,pageSize??50),ct),http));",
        "group.MapGet(\"/projects/{projectId}/backlog\",async(stringprojectId,string?after,int?pageSize,ListSprintBacklogHandlerhandler,HttpContexthttp,CancellationTokenct)=>"
        + "Ok(awaithandler.HandleAsync(newListSprintBacklogQuery(projectId,after,pageSize??50),ct),http));",
        "group.MapGet(\"/projects/{projectId}/velocity\",async(stringprojectId,int?sprintCount,GetSprintVelocityHandlerhandler,HttpContexthttp,CancellationTokenct)=>"
        + "Ok((awaithandler.HandleAsync(newGetSprintVelocityQuery(projectId,sprintCount??6),ct)).Data,http));",
        "group.MapGet(\"/reports/sprint-burndown/{projectId}/{sprintId}\",async(stringprojectId,stringsprintId,DateOnlystartDate,DateOnlyendDate,GetSprintBurndownHandlerhandler,HttpContexthttp,CancellationTokenct)=>"
        + "ReportOk(awaithandler.HandleAsync(newGetSprintBurndownQuery(projectId,sprintId,startDate,endDate),ct),http)).RequireRateLimiting(\"report\");",
        "group.MapGet(\"/reports/sprint-velocity/{projectId}\",async(stringprojectId,int?sprintCount,GetSprintVelocityHandlerhandler,HttpContexthttp,CancellationTokenct)=>"
        + "ReportOk(awaithandler.HandleAsync(newGetSprintVelocityQuery(projectId,sprintCount??6),ct),http)).RequireRateLimiting(\"report\");",
        "group.MapGet(\"/{projectId}\",async(stringprojectId,GetWorkItemTypeSchemaHandlerhandler,HttpContexthttp,CancellationTokenct)=>"
        + "Ok(awaithandler.HandleAsync(newGetWorkItemTypeSchemaQuery(projectId),ct),http));",
        "group.MapGet(\"/{projectId}/reports/custom-fields/{fieldKey}\",async(stringprojectId,stringfieldKey,GetCustomFieldDistributionHandlerhandler,HttpContexthttp,CancellationTokenct)=>"
        + "Ok(awaithandler.HandleAsync(newGetCustomFieldDistributionQuery(projectId,fieldKey),ct),http));",
        "group.MapGet(\"/{projectId}/reports/issue-types\",async(stringprojectId,GetIssueTypeDistributionHandlerhandler,HttpContexthttp,CancellationTokenct)=>"
        + "Ok(awaithandler.HandleAsync(newGetIssueTypeDistributionQuery(projectId),ct),http));",
        "group.MapGet(\"/{sprintId}\",async(stringsprintId,GetSprintHandlerhandler,HttpContexthttp,CancellationTokenct)=>"
        + "Ok(awaithandler.HandleAsync(newGetSprintQuery(sprintId),ct),http));",
        "group.MapGet(\"/{sprintId}/burndown\",async(stringsprintId,DateOnly?startDate,DateOnly?endDate,GetSprintHandlergetSprint,GetSprintBurndownHandlerburndown,HttpContexthttp,CancellationTokenct)=>"
        + "{varsprint=awaitgetSprint.HandleAsync(newGetSprintQuery(sprintId),ct);varsnapshot=awaitburndown.HandleAsync(newGetSprintBurndownQuery(sprint.ProjectId,sprintId,startDate,endDate),ct);returnOk(snapshot.Data,http);});",
        "group.MapPost(\"\",async(CreateSprintRequestrequest,CreateSprintHandlerhandler,HttpContexthttp,CancellationTokenct)=>"
        + "Created(awaithandler.HandleAsync(newCreateSprintCommand(request,CorrelationId(http)),ct),http)).WithZumboPermission(PermissionCatalog.WorkItemUpdate);",
        "group.MapPost(\"/{sprintId}/complete\",async(stringsprintId,CompleteSprintRequestrequest,CompleteSprintHandlerhandler,HttpContexthttp,CancellationTokenct)=>"
        + "Ok(awaithandler.HandleAsync(newCompleteSprintCommand(sprintId,request,CorrelationId(http)),ct),http)).WithZumboPermission(PermissionCatalog.WorkItemUpdate);",
        "group.MapPost(\"/{sprintId}/start\",async(stringsprintId,StartSprintHandlerhandler,HttpContexthttp,CancellationTokenct)=>"
        + "Ok(awaithandler.HandleAsync(newStartSprintCommand(sprintId,CorrelationId(http)),ct),http)).WithZumboPermission(PermissionCatalog.WorkItemUpdate);",
        "group.MapPut(\"/{projectId}\",async(stringprojectId,UpsertWorkItemTypeSchemaRequestrequest,UpsertWorkItemTypeSchemaHandlerhandler,HttpContexthttp,CancellationTokenct)=>"
        + "Ok(awaithandler.HandleAsync(newUpsertWorkItemTypeSchemaCommand(projectId,request,CorrelationId(http)),ct),http)).WithZumboPermission(PermissionCatalog.WorkItemUpdate);",
        "group.MapPut(\"/{sprintId}/items/{workItemId}\",async(stringsprintId,stringworkItemId,PlanSprintWorkItemRequestrequest,PlanSprintWorkItemHandlerhandler,HttpContexthttp,CancellationTokenct)=>"
        + "Ok(awaithandler.HandleAsync(newPlanSprintWorkItemCommand(sprintId,workItemId,request,CorrelationId(http)),ct),http)).WithZumboPermission(PermissionCatalog.WorkItemUpdate);",
        "group.MapGet(\"/templates\",async(stringprojectId,int?page,int?pageSize,bool?includeArchived,ListWorkItemTemplatesHandlerhandler,HttpContexthttp,CancellationTokenct)=>"
        + "Ok(awaithandler.HandleAsync(newListWorkItemTemplatesQuery(projectId,page??1,pageSize??50,includeArchived??false),ct),http));",
        "group.MapGet(\"/recurrences\",async(stringprojectId,int?page,int?pageSize,bool?includeArchived,ListWorkItemRecurrencesHandlerhandler,HttpContexthttp,CancellationTokenct)=>"
        + "Ok(awaithandler.HandleAsync(newListWorkItemRecurrencesQuery(projectId,page??1,pageSize??50,includeArchived??false),ct),http));",
        "group.MapGet(\"/recurrences/{recurrenceId}/occurrences\",async(stringrecurrenceId,int?page,int?pageSize,ListRecurrenceOccurrencesHandlerhandler,HttpContexthttp,CancellationTokenct)=>"
        + "Ok(awaithandler.HandleAsync(newListRecurrenceOccurrencesQuery(recurrenceId,page??1,pageSize??50),ct),http));",
        "group.MapPost(\"/recurrences/preview\",async(PreviewWorkItemRecurrenceRequestrequest,PreviewWorkItemRecurrenceHandlerhandler,HttpContexthttp,CancellationTokenct)=>"
        + "Ok(awaithandler.HandleAsync(newPreviewWorkItemRecurrenceQuery(request),ct),http)).WithZumboPermission(PermissionCatalog.WorkItemCreate);",
        "group.MapPost(\"/recurrences\",async(CreateWorkItemRecurrenceRequestrequest,CreateWorkItemRecurrenceHandlerhandler,HttpContexthttp,CancellationTokenct)=>"
        + "Created(awaithandler.HandleAsync(newCreateWorkItemRecurrenceCommand(request,CorrelationId(http)),ct),http)).WithZumboPermission(PermissionCatalog.WorkItemCreate);",
        "group.MapPatch(\"/recurrences/{recurrenceId}/state\",async(stringrecurrenceId,SetWorkItemRecurrenceStateRequestrequest,SetWorkItemRecurrenceStateHandlerhandler,HttpContexthttp,CancellationTokenct)=>"
        + "Ok(awaithandler.HandleAsync(newSetWorkItemRecurrenceStateCommand(recurrenceId,request.Active,CorrelationId(http)),ct),http)).WithZumboPermission(PermissionCatalog.WorkItemUpdate);",
        "group.MapDelete(\"/recurrences/{recurrenceId}\",async(stringrecurrenceId,ArchiveWorkItemRecurrenceHandlerhandler,HttpContexthttp,CancellationTokenct)=>"
        + "{awaithandler.HandleAsync(newArchiveWorkItemRecurrenceCommand(recurrenceId,CorrelationId(http)),ct);returnResults.NoContent();}).WithZumboPermission(PermissionCatalog.WorkItemUpdate);",
        "group.MapPost(\"/templates\",async(CreateWorkItemTemplateRequestrequest,CreateWorkItemTemplateHandlerhandler,HttpContexthttp,CancellationTokenct)=>"
        + "Created(awaithandler.HandleAsync(newCreateWorkItemTemplateCommand(request,CorrelationId(http)),ct),http)).WithZumboPermission(PermissionCatalog.WorkItemCreate);",
        "group.MapPut(\"/templates/{templateId}\",async(stringtemplateId,UpdateWorkItemTemplateRequestrequest,UpdateWorkItemTemplateHandlerhandler,HttpContexthttp,CancellationTokenct)=>"
        + "Ok(awaithandler.HandleAsync(newUpdateWorkItemTemplateCommand(templateId,request,CorrelationId(http)),ct),http)).WithZumboPermission(PermissionCatalog.WorkItemUpdate);",
        "group.MapDelete(\"/templates/{templateId}\",async(stringtemplateId,ArchiveWorkItemTemplateHandlerhandler,HttpContexthttp,CancellationTokenct)=>"
        + "{awaithandler.HandleAsync(newArchiveWorkItemTemplateCommand(templateId,CorrelationId(http)),ct);returnResults.NoContent();}).WithZumboPermission(PermissionCatalog.WorkItemUpdate);",
        "group.MapPost(\"/recurrences/process-due\",async(ScheduleDueRecurrencesHandlerhandler,CancellationTokenct)=>"
        + "Results.Ok(new{scheduled=awaithandler.HandleAsync(newScheduleDueRecurrencesCommand(),ct)})).WithZumboPermission(PermissionCatalog.OperationsManage,isGlobal:true);",
        "group.MapGet(\"/{documentId}\",async(stringdocumentId,bool?includeArchived,[FromServices]GetKnowledgeDocumentHandlerhandler,HttpContexthttp,CancellationTokenct)=>"
        + "Ok(awaithandler.HandleAsync(newGetKnowledgeDocumentQuery(documentId,includeArchived??false),ct),http));",
        "group.MapGet(\"\",async(string?query,string?scopeType,string?scopeId,bool?includeArchived,int?page,int?pageSize,[FromServices]SearchKnowledgeDocumentsHandlerhandler,HttpContexthttp,CancellationTokenct)=>"
        + "Ok(awaithandler.HandleAsync(newSearchKnowledgeDocumentsQuery(query,scopeType,scopeId,includeArchived??false,page??1,pageSize??50),ct),http)).RequireRateLimiting(\"search\");",
        "group.MapGet(\"/scope-link-options\",async(stringscopeType,stringscopeId,string?query,[FromServices]GetKnowledgeLinkOptionsHandlerhandler,HttpContexthttp,CancellationTokenct)=>"
        + "Ok(awaithandler.HandleAsync(newGetKnowledgeLinkOptionsQuery(scopeType,scopeId,query),ct),http)).RequireRateLimiting(\"search\");",
        "group.MapGet(\"/{documentId}/versions/{number:int}\",async(stringdocumentId,intnumber,[FromServices]GetKnowledgeVersionHandlerhandler,HttpContexthttp,CancellationTokenct)=>"
        + "Ok(awaithandler.HandleAsync(newGetKnowledgeVersionQuery(documentId,number),ct),http));",
        "group.MapPost(\"/{documentId}/comments\",async(stringdocumentId,AddKnowledgeCommentRequestrequest,[FromServices]AddKnowledgeCommentHandlerhandler,HttpContexthttp,CancellationTokenct)=>"
        + "Ok(awaithandler.HandleAsync(newAddKnowledgeCommentCommand(documentId,request,CorrelationId(http)),ct),http));",
        "group.MapPatch(\"/{documentId}/comments/{commentId}/resolve\",async(stringdocumentId,stringcommentId,[FromServices]ResolveKnowledgeCommentHandlerhandler,HttpContexthttp,CancellationTokenct)=>"
        + "Ok(awaithandler.HandleAsync(newResolveKnowledgeCommentCommand(documentId,commentId,CorrelationId(http)),ct),http));",
        "group.MapPost(\"\",async(CreateKnowledgeDocumentRequestrequest,[FromServices]CreateKnowledgeDocumentHandlerhandler,HttpContexthttp,CancellationTokenct)=>"
        + "Ok(awaithandler.HandleAsync(newCreateKnowledgeDocumentCommand(request,CorrelationId(http)),ct),http));",
        "group.MapPut(\"/{documentId}\",async(stringdocumentId,CreateKnowledgeVersionRequestrequest,[FromServices]AddKnowledgeVersionHandlerhandler,HttpContexthttp,CancellationTokenct)=>"
        + "Ok(awaithandler.HandleAsync(newAddKnowledgeVersionCommand(documentId,request,CorrelationId(http)),ct),http));",
        "group.MapDelete(\"/{documentId}\",async(stringdocumentId,[FromServices]ArchiveKnowledgeDocumentHandlerhandler,HttpContexthttp,CancellationTokenct)=>"
        + "{awaithandler.HandleAsync(newArchiveKnowledgeDocumentCommand(documentId,CorrelationId(http)),ct);returnOk(new{archived=true},http);});",
        "group.MapPost(\"/login\",async(LoginRequestrequest,LoginHandlerhandler,HttpContexthttp,CancellationTokenct)=>"
        + "Ok(awaithandler.HandleAsync(request,ct),http)).RequireRateLimiting(\"login\");",
        "group.MapPost(\"/logout\",async(LogoutRequestrequest,LogoutHandlerhandler,HttpContexthttp,CancellationTokenct)=>"
        + "Ok(awaithandler.HandleAsync(request,ct),http));",
        "group.MapPost(\"/refresh\",async(RefreshTokenRequestrequest,RefreshTokenHandlerhandler,HttpContexthttp,CancellationTokenct)=>"
        + "Ok(awaithandler.HandleAsync(request,ct),http));",
        "group.MapPost(\"/change-password\",async(ChangePasswordRequestrequest,ChangePasswordHandlerhandler,HttpContexthttp,CancellationTokenct)=>"
        + "Ok(awaithandler.HandleAsync(request,CorrelationId(http),ct),http))"
        + ".RequireAuthorization().WithZumboPermission(PermissionCatalog.ProfileRead);",
        "group.MapPost(\"/forgot-password\",async(ForgotPasswordRequestrequest,ForgotPasswordHandlerhandler,HttpContexthttp,CancellationTokenct)=>"
        + "Ok(awaithandler.HandleAsync(request,ct),http)).RequireRateLimiting(\"password-reset\");",
        "group.MapPost(\"/reset-password\",async(ResetPasswordRequestrequest,ResetPasswordHandlerhandler,HttpContexthttp,CancellationTokenct)=>"
        + "Ok(awaithandler.HandleAsync(request,CorrelationId(http),ct),http)).RequireRateLimiting(\"password-reset\");",
        "group.MapGet(\"/sessions\",async(ListSessionsHandlerhandler,HttpContexthttp,CancellationTokenct)=>"
        + "Ok(awaithandler.HandleAsync(http.User.FindFirst(\"sessionId\")?.Value,ct),http))"
        + ".RequireAuthorization().WithZumboPermission(PermissionCatalog.ProfileRead);",
        "group.MapDelete(\"/sessions/{sessionId}\",async(stringsessionId,RevokeSessionHandlerhandler,HttpContexthttp,CancellationTokenct)=>"
        + "{awaithandler.HandleAsync(sessionId,CorrelationId(http),ct);returnOk(new{revoked=true},http);})"
        + ".RequireAuthorization().WithZumboPermission(PermissionCatalog.ProfileRead);",
        "group.MapGet(\"/mfa\",async(GetMfaStatusHandlerhandler,HttpContexthttp,CancellationTokenct)=>"
        + "Ok(awaithandler.HandleAsync(ct),http)).RequireAuthorization()"
        + ".WithZumboPermission(PermissionCatalog.ProfileRead);",
        "group.MapPost(\"/mfa/setup\",async(BeginMfaSetupRequestrequest,BeginMfaSetupHandlerhandler,HttpContexthttp,CancellationTokenct)=>"
        + "Ok(awaithandler.HandleAsync(request,CorrelationId(http),ct),http)).RequireAuthorization()"
        + ".WithZumboPermission(PermissionCatalog.ProfileRead);",
        "group.MapPost(\"/mfa/confirm\",async(ConfirmMfaSetupRequestrequest,ConfirmMfaSetupHandlerhandler,HttpContexthttp,CancellationTokenct)=>"
        + "Ok(awaithandler.HandleAsync(request,CorrelationId(http),ct),http)).RequireAuthorization()"
        + ".WithZumboPermission(PermissionCatalog.ProfileRead);",
        "group.MapPost(\"/mfa/disable\",async(DisableMfaRequestrequest,DisableMfaHandlerhandler,HttpContexthttp,CancellationTokenct)=>"
        + "Ok(awaithandler.HandleAsync(request,CorrelationId(http),ct),http)).RequireAuthorization()"
        + ".WithZumboPermission(PermissionCatalog.ProfileRead);",
        "group.MapPost(\"/mfa/recovery-codes\",async(RegenerateMfaRecoveryCodesRequestrequest,RegenerateMfaRecoveryCodesHandlerhandler,HttpContexthttp,CancellationTokenct)=>"
        + "Ok(awaithandler.HandleAsync(request,CorrelationId(http),ct),http)).RequireAuthorization()"
        + ".WithZumboPermission(PermissionCatalog.ProfileRead);",
        "group.MapPost(\"/deactivate\",async(DeactivateAccountRequestrequest,DeactivateAccountHandlerhandler,HttpContexthttp,CancellationTokenct)=>"
        + "Ok(awaithandler.HandleAsync(request,ct),http)).RequireAuthorization()"
        + ".WithZumboPermission(PermissionCatalog.ProfileRead);",
        "group.MapGet(\"/runs/{runId}\",async(stringrunId,GetAutomationRunHandlerhandler,HttpContexthttp,CancellationTokenct)=>"
        + "Ok(awaithandler.HandleAsync(newGetAutomationRunQuery(runId),ct),http));",
        "group.MapGet(\"/runs\",async(stringprojectId,string?ruleId,string?status,int?page,int?pageSize,ListAutomationRunsHandlerhandler,HttpContexthttp,CancellationTokenct)=>"
        + "Ok(awaithandler.HandleAsync(newListAutomationRunsQuery(projectId,ruleId,status,page??1,pageSize??50),ct),http));",
        "group.MapPost(\"/runs/{runId}/replay\",async(stringrunId,ReplayAutomationRunHandlerhandler,HttpContexthttp,CancellationTokenct)=>"
        + "Ok(awaithandler.HandleAsync(newReplayAutomationRunCommand(runId,CorrelationId(http)),ct),http))"
        + ".WithZumboPermission(PermissionCatalog.WorkflowManage);",
        "group.MapPut(\"/{boardId}\",async(stringboardId,UpdateBoardRequestrequest,UpdateBoardHandlerhandler,HttpContexthttp,CancellationTokenct)=>"
        + "Ok(awaithandler.HandleAsync(boardId,request,CorrelationId(http),ct),http)).WithZumboPermission(PermissionCatalog.BoardManage);",
        "group.MapDelete(\"/{boardId}\",async(stringboardId,ArchiveBoardHandlerhandler,HttpContexthttp,CancellationTokenct)=>"
        + "{awaithandler.HandleAsync(newArchiveBoardCommand(boardId,CorrelationId(http)),ct);returnOk(new{archived=true},http);})"
        + ".WithZumboPermission(PermissionCatalog.BoardManage);",
        "group.MapPost(\"/{boardId}/restore\",async(stringboardId,RestoreBoardHandlerhandler,HttpContexthttp,CancellationTokenct)=>"
        + "Ok(awaithandler.HandleAsync(newRestoreBoardCommand(boardId,CorrelationId(http)),ct),http))"
        + ".WithZumboPermission(PermissionCatalog.BoardManage);",
        "group.MapPatch(\"/{boardId}/swimlane\",async(stringboardId,UpdateSwimlaneRequestrequest,UpdateSwimlaneHandlerhandler,HttpContexthttp,CancellationTokenct)=>"
        + "Ok(awaithandler.HandleAsync(boardId,request,CorrelationId(http),ct),http))"
        + ".WithZumboPermission(PermissionCatalog.BoardManage);",
        "group.MapPost(\"/{boardId}/columns\",async(stringboardId,CreateColumnRequestrequest,AddColumnHandlerhandler,HttpContexthttp,CancellationTokenct)=>"
        + "Ok(awaithandler.HandleAsync(boardId,request,CorrelationId(http),ct),http))"
        + ".WithZumboPermission(PermissionCatalog.BoardManage);",
        "group.MapPut(\"/{boardId}/columns/{columnId}\",async(stringboardId,stringcolumnId,UpdateColumnRequestrequest,UpdateColumnHandlerhandler,HttpContexthttp,CancellationTokenct)=>"
        + "Ok(awaithandler.HandleAsync(boardId,columnId,request,CorrelationId(http),ct),http))"
        + ".WithZumboPermission(PermissionCatalog.BoardManage);",
        "group.MapDelete(\"/{boardId}/columns/{columnId}\",async(stringboardId,stringcolumnId,DeleteColumnHandlerhandler,HttpContexthttp,CancellationTokenct)=>"
        + "Ok(awaithandler.HandleAsync(newDeleteColumnCommand(boardId,columnId,CorrelationId(http)),ct),http))"
        + ".WithZumboPermission(PermissionCatalog.BoardManage);",
        "group.MapPut(\"/{boardId}/columns/reorder\",async(stringboardId,ReorderColumnsRequestrequest,ReorderColumnsHandlerhandler,HttpContexthttp,CancellationTokenct)=>"
        + "Ok(awaithandler.HandleAsync(boardId,request,CorrelationId(http),ct),http))"
        + ".WithZumboPermission(PermissionCatalog.BoardManage);",
        "group.MapPost(\"/{boardId}/views\",async(stringboardId,CreateBoardViewRequestrequest,CreateViewHandlerhandler,HttpContexthttp,CancellationTokenct)=>"
        + "Ok(awaithandler.HandleAsync(boardId,request,CorrelationId(http),ct),http))"
        + ".WithZumboPermission(PermissionCatalog.BoardManage);",
        "group.MapPut(\"/{boardId}/views/{viewId}\",async(stringboardId,stringviewId,UpdateBoardViewRequestrequest,UpdateViewHandlerhandler,HttpContexthttp,CancellationTokenct)=>"
        + "Ok(awaithandler.HandleAsync(boardId,viewId,request,CorrelationId(http),ct),http))"
        + ".WithZumboPermission(PermissionCatalog.BoardManage);",
        "group.MapDelete(\"/{boardId}/views/{viewId}\",async(stringboardId,stringviewId,DeleteViewHandlerhandler,HttpContexthttp,CancellationTokenct)=>"
        + "Ok(awaithandler.HandleAsync(boardId,viewId,CorrelationId(http),ct),http))"
        + ".WithZumboPermission(PermissionCatalog.BoardManage);",
        "group.MapDelete(\"/{planId}\",async(stringplanId,[FromServices]ArchiveCapacityPlanHandlerhandler,HttpContexthttp,CancellationTokenct)=>"
        + "{awaithandler.HandleAsync(newArchiveCapacityPlanCommand(planId,CorrelationId(http)),ct);returnOk(new{archived=true},http);});",
        "group.MapGet(\"/{planId}\",async(stringplanId,bool?includeArchived,[FromServices]GetCapacityPlanHandlerhandler,HttpContexthttp,CancellationTokenct)=>"
        + "Ok(awaithandler.HandleAsync(newGetCapacityPlanQuery(planId,includeArchived??false),ct),http));",
        "group.MapGet(\"\",async(bool?includeArchived,int?page,int?pageSize,[FromServices]ListCapacityPlansHandlerhandler,HttpContexthttp,CancellationTokenct)=>"
        + "Ok(awaithandler.HandleAsync(newListCapacityPlansQuery(includeArchived??false,page??1,pageSize??50),ct),http));",
        "group.MapPut(\"/{planId}/sharing\",async(stringplanId,ShareCapacityPlanRequestrequest,[FromServices]ShareCapacityPlanHandlerhandler,HttpContexthttp,CancellationTokenct)=>"
        + "Ok(awaithandler.HandleAsync(newShareCapacityPlanCommand(planId,request,CorrelationId(http)),ct),http));",
        "group.MapPost(\"\",async(SaveCapacityPlanRequestrequest,[FromServices]SaveCapacityPlanHandlerhandler,HttpContexthttp,CancellationTokenct)=>"
        + "Ok(awaithandler.HandleAsync(newSaveCapacityPlanCommand(null,request,CorrelationId(http)),ct),http));",
        "group.MapPut(\"/{planId}\",async(stringplanId,SaveCapacityPlanRequestrequest,[FromServices]SaveCapacityPlanHandlerhandler,HttpContexthttp,CancellationTokenct)=>"
        + "Ok(awaithandler.HandleAsync(newSaveCapacityPlanCommand(planId,request,CorrelationId(http)),ct),http));",
        "group.MapGet(\"/{planId}/snapshot\",async(stringplanId,[FromServices]GetCapacitySnapshotHandlerhandler,HttpContexthttp,CancellationTokenct)=>"
        + "Ok(awaithandler.HandleAsync(newGetCapacitySnapshotQuery(planId),ct),http)).RequireRateLimiting(\"report\");",
        "group.MapPost(\"/{planId}/scenarios\",async(stringplanId,CapacityScenarioRequestrequest,[FromServices]PreviewScenarioHandlerhandler,HttpContexthttp,CancellationTokenct)=>"
        + "Ok(awaithandler.HandleAsync(newPreviewScenarioQuery(planId,request),ct),http)).RequireRateLimiting(\"report\");",
        "group.MapGet(\"/\",async(ListWebhookSubscriptionsHandlerhandler,HttpContexthttp,CancellationTokenct)=>"
        + "Ok(awaithandler.HandleAsync(newListWebhookSubscriptionsQuery(),ct),http));",
        "group.MapGet(\"/{id}\",async(stringid,GetWebhookSubscriptionHandlerhandler,HttpContexthttp,CancellationTokenct)=>"
        + "Ok(awaithandler.HandleAsync(newGetWebhookSubscriptionQuery(id),ct),http));",
        "group.MapGet(\"/{id}\",async(stringid,GetWorkItemHandlerhandler,HttpContexthttp,CancellationTokenct)=>"
        + "Ok(awaithandler.HandleAsync(newGetWorkItemQuery(id),ct),http));",
        "group.MapPost(\"/search\",async(WorkItemSearchRequestrequest,SearchWorkItemsHandlerhandler,HttpContexthttp,CancellationTokenct)=>"
        + "Ok(awaithandler.HandlePageAsync(request,ct),http)).RequireRateLimiting(\"search\");",
        "group.MapPost(\"/bulk/move\",async(BulkMoveWorkItemsRequestrequest,BulkMoveWorkItemsHandlerhandler,HttpContexthttp,CancellationTokenct)=>"
        + "Ok(awaithandler.HandleAsync(newBulkMoveWorkItemsCommand(request,CorrelationId(http)),ct),http)).WithZumboPermission(PermissionCatalog.WorkItemMove).RequireRateLimiting(\"bulk\");",
        "group.MapPost(\"/bulk/assign\",async(BulkAssignWorkItemsRequestrequest,BulkAssignWorkItemsHandlerhandler,HttpContexthttp,CancellationTokenct)=>"
        + "Ok(awaithandler.HandleAsync(newBulkAssignWorkItemsCommand(request,CorrelationId(http)),ct),http)).WithZumboPermission(PermissionCatalog.WorkItemAssign).RequireRateLimiting(\"bulk\");",
        "group.MapPost(\"/bulk/archive\",async(BulkArchiveWorkItemsRequestrequest,BulkArchiveWorkItemsHandlerhandler,HttpContexthttp,CancellationTokenct)=>"
        + "Ok(awaithandler.HandleAsync(newBulkArchiveWorkItemsCommand(request,CorrelationId(http)),ct),http)).WithZumboPermission(PermissionCatalog.WorkItemDelete).RequireRateLimiting(\"bulk\");",
        "group.MapPut(\"/{id}\",async(stringid,UpdateWorkItemRequestrequest,UpdateWorkItemHandlerhandler,HttpContexthttp,CancellationTokenct)=>"
        + "Ok(awaithandler.HandleAsync(newUpdateWorkItemCommand(id,request,CorrelationId(http)),ct),http))"
        + ".WithZumboPermission(PermissionCatalog.WorkItemUpdate);",
        "group.MapDelete(\"/{id}\",async(stringid,ArchiveWorkItemHandlerhandler,HttpContexthttp,CancellationTokenct)=>"
        + "{awaithandler.HandleAsync(newArchiveWorkItemCommand(id,CorrelationId(http)),ct);returnOk(new{archived=true},http);})"
        + ".WithZumboPermission(PermissionCatalog.WorkItemDelete);",
        "group.MapPost(\"/{id}/restore\",async(stringid,RestoreWorkItemHandlerhandler,HttpContexthttp,CancellationTokenct)=>"
        + "Ok(awaithandler.HandleAsync(newRestoreWorkItemCommand(id,CorrelationId(http)),ct),http))"
        + ".WithZumboPermission(PermissionCatalog.WorkItemDelete);",
        "group.MapPost(\"/{id}/labels\",async(stringid,AddLabelRequestrequest,AddLabelHandlerhandler,HttpContexthttp,CancellationTokenct)=>"
        + "Ok(awaithandler.HandleAsync(newAddLabelCommand(id,request),ct),http))"
        + ".WithZumboPermission(PermissionCatalog.WorkItemUpdate);",
        "group.MapDelete(\"/{id}/labels/{label}\",async(stringid,stringlabel,RemoveLabelHandlerhandler,HttpContexthttp,CancellationTokenct)=>"
        + "Ok(awaithandler.HandleAsync(newRemoveLabelCommand(id,label),ct),http))"
        + ".WithZumboPermission(PermissionCatalog.WorkItemUpdate);",
        "group.MapPost(\"/{id}/comments\",async(stringid,AddCommentRequestrequest,AddCommentHandlerhandler,HttpContexthttp,CancellationTokenct)=>"
        + "Ok(awaithandler.HandleAsync(newAddCommentCommand(id,request,CorrelationId(http)),ct),http))"
        + ".WithZumboPermission(PermissionCatalog.CommentCreate);",
        "group.MapPut(\"/{id}/comments/{commentId}\",async(stringid,stringcommentId,EditCommentRequestrequest,EditCommentHandlerhandler,HttpContexthttp,CancellationTokenct)=>"
        + "Ok(awaithandler.HandleAsync(newEditCommentCommand(id,commentId,request,CorrelationId(http)),ct),http))"
        + ".WithZumboPermission(PermissionCatalog.CommentCreate);",
        "group.MapDelete(\"/{id}/comments/{commentId}\",async(stringid,stringcommentId,DeleteCommentHandlerhandler,HttpContexthttp,CancellationTokenct)=>"
        + "Ok(awaithandler.HandleAsync(newDeleteCommentCommand(id,commentId,CorrelationId(http)),ct),http))"
        + ".WithZumboPermission(PermissionCatalog.CommentCreate);",
        "group.MapPatch(\"/{id}/parent\",async(stringid,SetWorkItemParentRequestrequest,SetParentHandlerhandler,HttpContexthttp,CancellationTokenct)=>"
        + "Ok(awaithandler.HandleAsync(newSetParentCommand(id,request,CorrelationId(http)),ct),http))"
        + ".WithZumboPermission(PermissionCatalog.WorkItemUpdate);",
        "group.MapPost(\"/{id}/relations\",async(stringid,LinkWorkItemRequestrequest,LinkWorkItemHandlerhandler,HttpContexthttp,CancellationTokenct)=>"
        + "Ok(awaithandler.HandleAsync(newLinkWorkItemCommand(id,request,CorrelationId(http)),ct),http))"
        + ".WithZumboPermission(PermissionCatalog.WorkItemLink);",
        "group.MapDelete(\"/{id}/relations/{relatedWorkItemId}\",async(stringid,stringrelatedWorkItemId,stringrelationType,UnlinkWorkItemHandlerhandler,HttpContexthttp,CancellationTokenct)=>"
        + "Ok(awaithandler.HandleAsync(newUnlinkWorkItemCommand(id,relatedWorkItemId,relationType,CorrelationId(http)),ct),http))"
        + ".WithZumboPermission(PermissionCatalog.WorkItemLink);",
        "group.MapPost(\"/{id}/attachments/upload\",async(stringid,IFormFilefile,UploadAttachmentHandlerhandler,HttpContexthttp,CancellationTokenct)=>{awaitusingvarstream=file.OpenReadStream();returnOk(awaithandler.HandleAsync(newUploadAttachmentCommand(id,stream,file.FileName,file.ContentType,file.Length,CorrelationId(http)),ct),http);}).WithZumboPermission(PermissionCatalog.AttachmentCreate).DisableAntiforgery().RequireRateLimiting(\"upload\");",
        "group.MapDelete(\"/{id}/attachments/{attachmentId}\",async(stringid,stringattachmentId,DeleteAttachmentHandlerhandler,HttpContexthttp,CancellationTokenct)=>Ok(awaithandler.HandleAsync(newDeleteAttachmentCommand(id,attachmentId,CorrelationId(http)),ct),http)).WithZumboPermission(PermissionCatalog.AttachmentDelete);",
        "group.MapGet(\"/reports/project-summary/{projectId}\",async(stringprojectId,ProjectSummaryHandlerhandler,HttpContexthttp,CancellationTokenct)=>ReportOk(awaithandler.HandleAsync(newProjectSummaryQuery(projectId),ct),http)).RequireRateLimiting(\"report\");",
        "group.MapGet(\"/reports/status-distribution/{projectId}\",async(stringprojectId,StatusDistributionHandlerhandler,HttpContexthttp,CancellationTokenct)=>ReportOk(awaithandler.HandleAsync(newStatusDistributionQuery(projectId),ct),http)).RequireRateLimiting(\"report\");",
        "group.MapGet(\"/reports/user-workload/{projectId}\",async(stringprojectId,UserWorkloadHandlerhandler,HttpContexthttp,CancellationTokenct)=>ReportOk(awaithandler.HandleAsync(newUserWorkloadQuery(projectId),ct),http)).RequireRateLimiting(\"report\");",
        "group.MapGet(\"/reports/due-date-risks/{projectId}\",async(stringprojectId,int?days,DueDateRisksHandlerhandler,HttpContexthttp,CancellationTokenct)=>ReportOk(awaithandler.HandleAsync(newDueDateRisksQuery(projectId,days??14),ct),http)).RequireRateLimiting(\"report\");",
        "group.MapGet(\"/reports/flow-time/{projectId}\",async(stringprojectId,DateOnly?from,DateOnly?to,FlowTimeHandlerhandler,HttpContexthttp,CancellationTokenct)=>ReportOk(awaithandler.HandleAsync(newFlowTimeQuery(projectId,from,to),ct),http)).RequireRateLimiting(\"report\");",
        "group.MapGet(\"/reports/completion-rate/{projectId}\",async(stringprojectId,DateOnly?from,DateOnly?to,CompletionRateHandlerhandler,HttpContexthttp,CancellationTokenct)=>ReportOk(awaithandler.HandleAsync(newCompletionRateQuery(projectId,from,to),ct),http)).RequireRateLimiting(\"report\");",
        "group.MapGet(\"/reports/team-performance/{projectId}\",async(stringprojectId,DateOnly?from,DateOnly?to,TeamPerformanceHandlerhandler,HttpContexthttp,CancellationTokenct)=>ReportOk(awaithandler.HandleAsync(newTeamPerformanceQuery(projectId,from,to),ct),http)).RequireRateLimiting(\"report\");",
        "group.MapGet(\"/{id}/attachments/{attachmentId}/preview\",async(stringid,stringattachmentId,OpenAttachmentHandlerhandler,HttpContexthttp,CancellationTokenct)=>"
        + "{varattachment=awaithandler.HandleAsync(newOpenAttachmentQuery(id,attachmentId),ct);"
        + "if(!IsPreviewableContentType(attachment.ContentType)){awaitattachment.Content.DisposeAsync();returnResults.StatusCode(StatusCodes.Status415UnsupportedMediaType);}"
        + "http.Response.Headers.CacheControl=\"private, no-store\";http.Response.Headers.Pragma=\"no-cache\";"
        + "http.Response.Headers.ContentSecurityPolicy=\"sandbox; default-src 'none'\";"
        + "http.Response.Headers[\"Cross-Origin-Resource-Policy\"]=\"same-origin\";"
        + "http.Response.Headers.ContentDisposition=$\"inline; filename*=UTF-8''{Uri.EscapeDataString(attachment.FileName)}\";"
        + "returnResults.Stream(attachment.Content,attachment.ContentType,enableRangeProcessing:true);});",
        "group.MapGet(\"/{id}/attachments/{attachmentId}/download\",async(stringid,stringattachmentId,OpenAttachmentHandlerhandler,HttpContexthttp,CancellationTokenct)=>"
        + "{varattachment=awaithandler.HandleAsync(newOpenAttachmentQuery(id,attachmentId),ct);"
        + "http.Response.Headers.CacheControl=\"private, no-store\";http.Response.Headers.Pragma=\"no-cache\";"
        + "returnResults.File(attachment.Content,attachment.ContentType,attachment.FileName,enableRangeProcessing:true);});",
        "group.MapPost(\"/{id}/checklist\",async(stringid,AddChecklistItemRequestrequest,AddChecklistItemHandlerhandler,HttpContexthttp,CancellationTokenct)=>"
        + "Ok(awaithandler.HandleAsync(newAddChecklistItemCommand(id,request),ct),http))"
        + ".WithZumboPermission(PermissionCatalog.WorkItemUpdate);",
        "group.MapPatch(\"/{id}/checklist/{itemId}\",async(stringid,stringitemId,CompleteChecklistItemRequestrequest,CompleteChecklistItemHandlerhandler,HttpContexthttp,CancellationTokenct)=>"
        + "Ok(awaithandler.HandleAsync(newCompleteChecklistItemCommand(id,itemId,request),ct),http))"
        + ".WithZumboPermission(PermissionCatalog.WorkItemUpdate);",
        "group.MapPost(\"/{id}/worklogs\",async(stringid,AddWorkLogRequestrequest,AddWorkLogHandlerhandler,HttpContexthttp,CancellationTokenct)=>"
        + "Ok(awaithandler.HandleAsync(newAddWorkLogCommand(id,request),ct),http))"
        + ".WithZumboPermission(PermissionCatalog.WorkLogCreate);",
        "group.MapPatch(\"/{id}/planning\",async(stringid,SetWorkItemPlanningRequestrequest,SetPlanningHandlerhandler,HttpContexthttp,CancellationTokenct)=>"
        + "Ok(awaithandler.HandleAsync(newSetPlanningCommand(id,request),ct),http))"
        + ".WithZumboPermission(PermissionCatalog.WorkItemUpdate);",
        "group.MapPatch(\"/{id}/status\",async(stringid,MoveWorkItemRequestrequest,MoveWorkItemHandlerhandler,HttpContexthttp,CancellationTokenct)=>"
        + "Ok(awaithandler.HandleAsync(newMoveWorkItemCommand(id,request,CorrelationId(http)),ct),http))"
        + ".WithZumboPermission(PermissionCatalog.WorkItemMove);",
        "group.MapPatch(\"/{id}/rank\",async(stringid,ReorderWorkItemRequestrequest,ReorderWorkItemHandlerhandler,HttpContexthttp,CancellationTokenct)=>"
        + "Ok(awaithandler.HandleAsync(newReorderWorkItemCommand(id,request,CorrelationId(http)),ct),http))"
        + ".WithZumboPermission(PermissionCatalog.WorkItemMove);",
        "group.MapPatch(\"/{id}/assignee\",async(stringid,AssignWorkItemRequestrequest,AssignWorkItemHandlerhandler,HttpContexthttp,CancellationTokenct)=>"
        + "Ok(awaithandler.HandleAsync(newAssignWorkItemCommand(id,request,CorrelationId(http)),ct),http))"
        + ".WithZumboPermission(PermissionCatalog.WorkItemAssign);",
        "group.MapPatch(\"/{id}/team\",async(stringid,SetWorkItemTeamRequestrequest,SetWorkItemTeamHandlerhandler,HttpContexthttp,CancellationTokenct)=>"
        + "Ok(awaithandler.HandleAsync(newSetWorkItemTeamCommand(id,request,CorrelationId(http)),ct),http))"
        + ".WithZumboPermission(PermissionCatalog.WorkItemUpdate);",
        "group.MapPut(\"/{id}/custom-fields\",async(stringid,SetWorkItemCustomFieldsRequestrequest,SetCustomFieldsHandlerhandler,HttpContexthttp,CancellationTokenct)=>"
        + "Ok(awaithandler.HandleAsync(newSetCustomFieldsCommand(id,request,CorrelationId(http)),ct),http))"
        + ".WithZumboPermission(PermissionCatalog.WorkItemUpdate);",
        "group.MapPost(\"/{id}/approvals\",async(stringid,RequestWorkItemApprovalRequestrequest,RequestApprovalHandlerhandler,HttpContexthttp,CancellationTokenct)=>"
        + "Ok(awaithandler.HandleAsync(newRequestApprovalCommand(id,request,CorrelationId(http)),ct),http))"
        + ".WithZumboPermission(PermissionCatalog.WorkItemApprove);",
        "group.MapPost(\"/{id}/approvals/{approvalId}/decision\",async(stringid,stringapprovalId,DecideWorkItemApprovalRequestrequest,DecideApprovalHandlerhandler,HttpContexthttp,CancellationTokenct)=>"
        + "Ok(awaithandler.HandleAsync(newDecideApprovalCommand(id,approvalId,request,CorrelationId(http)),ct),http))"
        + ".WithZumboPermission(PermissionCatalog.WorkItemApprove);",
        "group.MapGet(\"/metrics\",async(GetWebhookDeliveryMetricsHandlerhandler,HttpContexthttp,CancellationTokenct)=>"
        + "Ok(awaithandler.HandleAsync(newGetWebhookDeliveryMetricsQuery(),ct),http));",
        "group.MapGet(\"/{id}/deliveries\",async(stringid,string?cursor,int?pageSize,ListWebhookDeliveriesHandlerhandler,HttpContexthttp,CancellationTokenct)=>"
        + "Ok(awaithandler.HandleAsync(newListWebhookDeliveriesQuery(id,cursor,pageSize??50),ct),http));",
        "group.MapGet(\"/deliveries/{deliveryId}\",async(stringdeliveryId,GetWebhookDeliveryHandlerhandler,HttpContexthttp,CancellationTokenct)=>"
        + "Ok(awaithandler.HandleAsync(newGetWebhookDeliveryQuery(deliveryId),ct),http));",
        "group.MapPost(\"/deliveries/{deliveryId}/replay\",async(stringdeliveryId,ReplayWebhookDeliveryHandlerhandler,HttpContexthttp,CancellationTokenct)=>"
        + "Ok(awaithandler.HandleAsync(newReplayWebhookDeliveryCommand(deliveryId,http.TraceIdentifier),ct),http)).RequireRateLimiting(\"bulk\");",
        "group.MapPost(\"/{id}/enable\",async(stringid,SetWebhookSubscriptionStateRequestrequest,SetSubscriptionStateHandlerhandler,HttpContexthttp,CancellationTokenct)=>"
        + "Ok(awaithandler.HandleAsync(newSetSubscriptionStateCommand(id,true,request,http.TraceIdentifier),ct),http));",
        "group.MapPost(\"/{id}/disable\",async(stringid,SetWebhookSubscriptionStateRequestrequest,SetSubscriptionStateHandlerhandler,HttpContexthttp,CancellationTokenct)=>"
        + "Ok(awaithandler.HandleAsync(newSetSubscriptionStateCommand(id,false,request,http.TraceIdentifier),ct),http));",
        "group.MapPut(\"/{id}\",async(stringid,UpdateWebhookSubscriptionRequestrequest,UpdateSubscriptionHandlerhandler,HttpContexthttp,CancellationTokenct)=>"
        + "Ok(awaithandler.HandleAsync(newUpdateSubscriptionCommand(id,request,http.TraceIdentifier),ct),http));",
        "group.MapPost(\"/\",async(CreateWebhookSubscriptionRequestrequest,CreateSubscriptionHandlerhandler,HttpContexthttp,CancellationTokenct)=>"
        + "Created(awaithandler.HandleAsync(newCreateSubscriptionCommand(request,http.TraceIdentifier),ct),http));",
        "group.MapPost(\"/{id}/rotate-secret\",async(stringid,RotateWebhookSecretRequestrequest,RotateSecretHandlerhandler,HttpContexthttp,CancellationTokenct)=>"
        + "Ok(awaithandler.HandleAsync(newRotateSecretCommand(id,request,http.TraceIdentifier),ct),http)).RequireRateLimiting(\"bulk\");",
        "group.MapPost(\"/{id}/test-delivery\",async(stringid,QueueTestDeliveryHandlerhandler,HttpContexthttp,CancellationTokenct)=>"
        + "Created(awaithandler.HandleAsync(newQueueTestDeliveryCommand(id,http.TraceIdentifier),ct),http)).RequireRateLimiting(\"bulk\");",
        "management.MapPost(\"/{connectionId}/health\",async(stringconnectionId,CheckProviderHealthHandlerhandler,HttpContexthttp,CancellationTokenct)=>"
        + "Ok(awaithandler.HandleAsync(newCheckProviderHealthCommand(connectionId,CorrelationId(http)),ct),http)).RequireRateLimiting(\"bulk\");",
        "management.MapGet(\"/{connectionId}/repositories\",async(stringconnectionId,ListRepositoriesHandlerhandler,HttpContexthttp,CancellationTokenct)=>"
        + "{varresult=awaithandler.HandleAsync(newListRepositoriesQuery(connectionId),ct);returnOk(newDevelopmentRepositoryPage("
        + "result.Items.Select(item=>newDevelopmentRepositoryResponse(item.ExternalRepositoryId,item.Name,item.FullName,item.Url,item.DefaultBranch))"
        + ".ToList(),result.Partial?\"Partial\":\"Complete\"),http);}).RequireRateLimiting(\"bulk\");",
        "management.MapGet(\"/\",async(ListConnectionsHandlerhandler,HttpContexthttp,CancellationTokenct)=>"
        + "Ok(awaithandler.HandleAsync(newListConnectionsQuery(),ct),http));",
        "management.MapGet(\"/{connectionId}\",async(stringconnectionId,GetConnectionHandlerhandler,HttpContexthttp,CancellationTokenct)=>"
        + "Ok(awaithandler.HandleAsync(newGetConnectionQuery(connectionId),ct),http));",
        "management.MapPost(\"/\",async(CreateDevelopmentConnectionRequestrequest,CreateConnectionHandlerhandler,HttpContexthttp,CancellationTokenct)=>"
        + "Created(awaithandler.HandleAsync(newCreateConnectionCommand(request,CorrelationId(http)),ct),http));",
        "management.MapPost(\"/{connectionId}/rotate-credential\",async(stringconnectionId,RotateDevelopmentCredentialRequestrequest,RotateCredentialHandlerhandler,HttpContexthttp,CancellationTokenct)=>"
        + "Ok(awaithandler.HandleAsync(newRotateCredentialCommand(connectionId,request,CorrelationId(http)),ct),http)).RequireRateLimiting(\"bulk\");",
        "management.MapPost(\"/{connectionId}/rotate-webhook-secret\",async(stringconnectionId,DevelopmentVersionRequestrequest,RotateWebhookSecretHandlerhandler,HttpContexthttp,CancellationTokenct)=>"
        + "Ok(awaithandler.HandleAsync(newRotateWebhookSecretCommand(connectionId,request,CorrelationId(http)),ct),http)).RequireRateLimiting(\"bulk\");",
        "management.MapPost(\"/{connectionId}/disconnect\",async(stringconnectionId,DevelopmentVersionRequestrequest,DisconnectConnectionHandlerhandler,HttpContexthttp,CancellationTokenct)=>"
        + "Ok(awaithandler.HandleAsync(newDisconnectConnectionCommand(connectionId,request,CorrelationId(http)),ct),http));",
        "management.MapDelete(\"/{connectionId}\",async(stringconnectionId,longexpectedVersion,DeleteConnectionHandlerhandler,HttpContexthttp,CancellationTokenct)=>"
        + "{awaithandler.HandleAsync(newDeleteConnectionCommand(connectionId,expectedVersion,CorrelationId(http)),ct);returnResults.NoContent();});",
        "management.MapGet(\"/{connectionId}/mappings\",async(stringconnectionId,ListConnectionMappingsHandlerhandler,HttpContexthttp,CancellationTokenct)=>"
        + "Ok(awaithandler.HandleAsync(newListConnectionMappingsQuery(connectionId),ct),http));",
        "management.MapPost(\"/{connectionId}/mappings\",async(stringconnectionId,CreateDevelopmentRepositoryMappingRequestrequest,CreateMappingHandlerhandler,HttpContexthttp,CancellationTokenct)=>"
        + "Created(awaithandler.HandleAsync(newCreateMappingCommand(connectionId,request,CorrelationId(http)),ct),http));",
        "management.MapDelete(\"/mappings/{mappingId}\",async(stringmappingId,longexpectedVersion,DeleteMappingHandlerhandler,HttpContexthttp,CancellationTokenct)=>"
        + "{awaithandler.HandleAsync(newDeleteMappingCommand(mappingId,expectedVersion,CorrelationId(http)),ct);returnResults.NoContent();});",
        "workItemLinks.MapGet(\"/mappings\",async(stringworkItemId,ListWorkItemMappingsHandlerhandler,HttpContexthttp,CancellationTokenct)=>"
        + "Ok(awaithandler.HandleAsync(newListWorkItemMappingsQuery(workItemId),ct),http)).WithZumboPermission(PermissionCatalog.WorkItemLink);",
        "workItemLinks.MapGet(\"/\",async(stringworkItemId,ListWorkItemLinksHandlerhandler,HttpContexthttp,CancellationTokenct)=>"
        + "Ok(awaithandler.HandleAsync(newListWorkItemLinksQuery(workItemId),ct),http));",
        "workItemLinks.MapPost(\"/\",async(stringworkItemId,CreateWorkItemDevelopmentLinkRequestrequest,CreateWorkItemLinkHandlerhandler,HttpContexthttp,CancellationTokenct)=>"
        + "Created(awaithandler.HandleAsync(newCreateWorkItemLinkCommand(workItemId,request,CorrelationId(http)),ct),http)).WithZumboPermission(PermissionCatalog.WorkItemLink);",
        "workItemLinks.MapDelete(\"/{linkId}\",async(stringworkItemId,stringlinkId,longexpectedVersion,DeleteWorkItemLinkHandlerhandler,HttpContexthttp,CancellationTokenct)=>"
        + "{awaithandler.HandleAsync(newDeleteWorkItemLinkCommand(workItemId,linkId,expectedVersion,CorrelationId(http)),ct);returnResults.NoContent();}).WithZumboPermission(PermissionCatalog.WorkItemLink);",
        "ingress.MapPost(\"/{connectionId}/webhook\",ReceiveWebhookWithHandlerAsync).AllowAnonymous();"
    ];

    private static string ProjectDirectory => Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".."));

    private static string RepositoryDirectory => Directory.GetParent(ProjectDirectory)!.FullName;

    [Fact]
    public void RefactorSnapshot_PreservesRuntimeContractsAndSeparatesIntentionalTimeoutChanges()
    {
        var baselineFiles = RefactorSourceReader.ReadGit(
            RepositoryDirectory,
            RefactorSemanticInventory.BaselineCommit);
        var targetFiles = RefactorSourceReader.ReadWorkingTree(ProjectDirectory);

        var endpoints = EndpointContracts(baselineFiles);
        var targetEndpoints = EndpointContracts(targetFiles);
        var registrations = DiContracts(baselineFiles);
        var targetRegistrations = DiContracts(targetFiles);
        var migrations = MigrationContracts(baselineFiles);
        var targetMigrations = MigrationContracts(targetFiles);
        var mongo = MongoContracts(baselineFiles);
        var targetMongo = MongoContracts(targetFiles);
        var serialization = SerializationContracts(baselineFiles);
        var targetSerialization = SerializationContracts(targetFiles);
        var messaging = MessagingContracts(baselineFiles);
        var targetMessaging = MessagingContracts(targetFiles);

        AssertExactWithAllowedReplacements(
            "HTTP endpoint mappings",
            endpoints,
            targetEndpoints,
            ReplacedVerticalSliceEndpointMappings,
            PortFocusedVerticalSliceEndpointMappings);
        AssertExactWithAllowedReplacements(
            "DI registrations",
            registrations,
            targetRegistrations,
            ReplacedVerticalSliceDiRegistrations,
            PortFocusedVerticalSliceDiRegistrations);
        AssertExact("PostgreSQL migrations", migrations, targetMigrations);
        AssertExactWithAllowedAdditions(
            "Mongo contracts",
            mongo,
            targetMongo,
            [
                "mongo.GetCollection<BsonDocument>(\"users\",\"Identity\")",
                "mongo.GetCollection<BsonDocument>(target.Collection,target.Module)",
                "mongo.GetCollection<BsonDocument>(target.Collection,target.Module)"
            ]);
        AssertExact("serialization attributes", serialization, targetSerialization);
        AssertExact("messaging contracts", messaging, targetMessaging);
        AssertConfigurationChangesAreIntentionalAndBounded();

        var report = RuntimeReport(
            endpoints.Count,
            registrations.Count,
            migrations.Count,
            mongo.Count,
            serialization.Count,
            messaging.Count);
        var reportPath = Path.Combine(ProjectDirectory, "docs", "architecture", "refactor-runtime-contracts.json");
        if (Environment.GetEnvironmentVariable("ZUMBO_UPDATE_REFACTOR_REPORTS") == "1")
        {
            File.WriteAllText(reportPath, report);
        }
        Assert.True(File.Exists(reportPath), "Missing generated runtime contract report.");
        Assert.Equal(report, File.ReadAllText(reportPath).Replace("\r\n", "\n", StringComparison.Ordinal));
    }

    private static string RuntimeReport(
        int endpoints,
        int registrations,
        int migrations,
        int mongo,
        int serialization,
        int messaging)
    {
        var payload = new
        {
            schemaVersion = 1,
            baselineCommit = RefactorSemanticInventory.BaselineCommit,
            refactorSnapshotCommit = RefactorSemanticInventory.RefactorSnapshotCommit,
            passed = true,
            exactContractCounts = new { endpoints, registrations, migrations, mongo, serialization, messaging },
            missingContracts = Array.Empty<string>(),
            changedContracts = Array.Empty<string>(),
            intentionalRuntimeContractAdditions = new[]
            {
                "Identity compatibility migration reads the users collection to normalize legacy document versions.",
                "Infrastructure marker cleanup resolves its module-owned collections for dry-run counting and idempotent updates."
            },
            intentionalRuntimeContractReplacements = new[]
            {
                "The template and recurrence read, preview, create, state, and archive handlers are scoped through port-focused constructors; their endpoints resolve them directly while the corresponding WorkItemTemplateRecurrenceService methods remain compatibility facades preserving the original public contracts.",
                "RegisterUserHandler and SearchUsersHandler remain scoped self-services; explicit factories select their port-focused constructors while compatibility constructors remain available.",
                "CreateOrganizationHandler and ListOrganizationsHandler remain scoped self-services; explicit factories select their port-focused constructors while compatibility constructors remain available.",
                "CreateTeamHandler and ListTeamsHandler remain scoped self-services; explicit factories select their port-focused constructors while compatibility constructors remain available.",
                "CreateProjectHandler and ListProjectsHandler remain scoped self-services; explicit factories select their port-focused constructors while compatibility constructors remain available.",
                "All nine Knowledge handlers are scoped through port-focused constructors; their routes resolve them directly while the corresponding KnowledgeService methods remain responsibility-grouped compatibility facades preserving their original public contracts.",
                "All eight Goal handlers are scoped through port-focused constructors; all ten Goal routes resolve them directly while the corresponding GoalService methods remain compatibility facades preserving their original public contracts.",
                "All eight Portfolio handlers are scoped through port-focused constructors; all eleven Portfolio routes resolve them directly while the corresponding PortfolioService methods remain compatibility facades preserving their original public contracts.",
                "CreateBoardHandler and ListBoardsByProjectHandler remain scoped self-services; explicit factories select their port-focused constructors while compatibility constructors remain available.",
                "UpsertWorkflowHandler and GetWorkflowHandler remain scoped self-services; explicit factories select their port-focused constructors while compatibility constructors remain available.",
                "CreateWorkItemHandler and SearchWorkItemsHandler remain scoped self-services; explicit factories select their port-focused constructors while compatibility constructors remain available.",
                "The intake work-item creator port resolves CreateIntakeWorkItemHandler over the port-focused create handler instead of WorkItemService; the explicit compatibility interface implementation remains available without serving the production intake caller.",
                "The paged work-item search route resolves SearchWorkItemsHandler directly while preserving request binding, rate limiting, response mapping, compatibility facade, and scoped port-focused registration.",
                "The bulk move, assign, and archive routes resolve feature handlers over their port-focused single-item handlers while preserving sequential execution, validation, item correlation IDs, per-item Zumbo error results, authorization, rate limiting, response mapping, and compatibility facades.",
                "WorkItemBulkJobProcessor resolves create, move, assign, and archive handlers through its port-focused constructor; its original WorkItemService constructor remains available for compatibility while job ownership, dry-run, idempotency, batching, progress, artifact, audit, retry, and cancellation behavior stays unchanged.",
                "AutomationWorkItemActionExecutor resolves read, assignment, label, priority, and comment handlers through its port-focused constructor; its original WorkItemService constructor remains available for compatibility while action no-op checks, command values, chain context, correlation and comment idempotency values, cancellation, and unsupported-action behavior stay unchanged.",
                "DashboardRenderer resolves all seven work-item report handlers through its port-focused constructor; its original WorkItemService constructor remains available for compatibility while dashboard ordering, filtering, source metadata, degradation behavior, formatting, and response aggregation stay unchanged.",
                "The single work-item read route resolves GetWorkItemHandler directly; its active-record filter, not-found contract, duplicate view-permission checks, organization-scoped activity hydration, response mapping, compatibility facade, and scoped registration remain preserved.",
                "The work-item archive route resolves ArchiveWorkItemHandler directly; its route, authorization metadata, correlation ID, active-record reads, project-structure lock, active-child rejection, WIP release, optimistic persistence, search deletion, audit, watcher activity, realtime publication, cache invalidation, compatibility facade, and scoped registration remain preserved.",
                "The work-item restore route resolves RestoreWorkItemHandler directly; its route, authorization metadata, correlation ID, archived-record reads, project-structure and placement locks, board placement, WIP capacity and reservation, rank allocation, optimistic persistence, search indexing, audit, watcher activity, realtime publication, cache invalidation, response mapping, compatibility facade, and scoped registration remain preserved.",
                "The work-item add-label route resolves AddLabelHandler directly; its route, authorization metadata, request binding, label validation and duplicate contract, optimistic persistence, search indexing, collaboration activity, watcher notification, automation event and chain context, response mapping, compatibility facade, automation-executor facade caller, and scoped registration remain preserved.",
                "The work-item remove-label route resolves RemoveLabelHandler directly; its route, authorization metadata, untrimmed route binding, case-insensitive removal and missing-label contract, optimistic persistence, search indexing, collaboration activity, watcher notification, automation event and chain context, response mapping, compatibility facade, automation-executor facade caller, and scoped registration remain preserved.",
                "The add-checklist-item route resolves AddChecklistItemHandler directly; its route, authorization metadata, request binding, text trimming behavior, generated checklist identity, optimistic persistence, collaboration activity, watcher notification, response mapping, compatibility facade, and scoped registration remain preserved.",
                "The complete-checklist-item route resolves CompleteChecklistItemHandler directly; its route, authorization metadata, request binding, exact checklist lookup and missing-item contract, complete-or-reopen detail, timestamp-derived activity identity, optimistic persistence, watcher notification, response mapping, compatibility facade, and scoped registration remain preserved.",
                "The add-work-log route resolves AddWorkLogHandler directly; its route, authorization metadata, request binding, hours validation, separated activity migration, work-log persistence, cache invalidation, collaboration activity, watcher notification, response mapping, compatibility facade, and scoped registration remain preserved.",
                "The set-planning route resolves SetPlanningHandler directly; its route, authorization metadata, request binding, sprint policy, aggregate planning rules, optimistic persistence, search indexing, collaboration activity, watcher notification, cache invalidation, response mapping, compatibility facade, and scoped registration remain preserved.",
                "ClearAssigneeHandler is a scoped self-service selected through an explicit port-focused factory; the automation action adapter retains its compatibility-facade call while the no-op behavior, persistence and all publication side effects remain preserved.",
                "The work-item assignee route resolves AssignWorkItemHandler directly; its route, authorization metadata, request binding, team eligibility, optimistic persistence, search, audit, assignee notification, watcher exclusion, realtime publication, cache invalidation, response mapping, compatibility facade, bulk and automation facade callers, and scoped registration remain preserved.",
                "The work-item team route resolves SetWorkItemTeamHandler directly; its route, authorization metadata, request binding, optional team normalization, team and assignee eligibility, unchanged-team conflict, optimistic persistence, audit, watcher activity, realtime publication, cache invalidation, response mapping, compatibility facade, and scoped registration remain preserved.",
                "The work-item custom-fields route resolves SetCustomFieldsHandler directly; its route, authorization metadata, request binding, type-schema validation, optimistic persistence, search, audit, watcher activity, realtime publication, cache invalidation, automation identity and chain context, response mapping, compatibility facade, and scoped registration remain preserved.",
                "The work-item approval-request route resolves RequestApprovalHandler directly; its route, authorization metadata, request binding, legacy activity separation, workflow transition and approval policy, active-approval conflict, optimistic and separated persistence, audit, watcher activity, response mapping, compatibility facade, unit-level facade caller, and scoped registration remain preserved.",
                "The work-item approval-decision route resolves DecideApprovalHandler directly; its route, authorization metadata, request binding, approval lookup and state checks, expiry persistence, self-approval guard, note normalization, optimistic and separated persistence, approval activity update, audit, watcher activity, requester notification, response mapping, compatibility facade, unit-level facade callers, and scoped registration remain preserved.",
                "The work-item core-update route resolves UpdateWorkItemHandler directly; its route, authorization metadata, request binding, title validation and normalization, optional description and priority handling, due-reminder reset, timestamp, optimistic persistence, search indexing, audit values, watcher activity, realtime publication, cache invalidation, automation identity and chain context, response mapping, compatibility facade, automation-adapter facade caller, unit-level facade callers, and scoped registration remain preserved.",
                "The work-item status route resolves MoveWorkItemHandler directly; its route, authorization metadata, request binding, double-read project locking, legacy activity separation, aggregate and workflow transition rules, board placement and rank, completion dependency checks, placement lock and WIP reservation, optimistic and separated persistence, approval and timeline activity updates, search, audit and workflow automation audit, watcher status notification, realtime publication, cache invalidation, automation identity and chain context, response mapping, compatibility facade, bulk and automation facade callers, unit-level facade callers, and scoped registration remain preserved.",
                "The work-item rank route resolves ReorderWorkItemHandler directly; its route, authorization metadata, request binding, double-read project locking, rank resolution and compaction, clock timestamp, optimistic and separated persistence, invariant audit values, watcher activity, realtime publication, response mapping, compatibility facade, unit-level facade caller, and scoped registration remain preserved.",
                "The work-item add-comment route resolves AddCommentHandler directly; its route, authorization metadata, request normalization, mention validation, legacy activity separation, deterministic idempotency identity and conflict behavior, comment limit, separated persistence, audit, mention and watcher notifications, automation identity and chain context, response mapping, compatibility facade, automation-adapter and unit-level facade callers, and scoped registration remain preserved.",
                "The work-item edit-comment route resolves EditCommentHandler directly; its route, authorization metadata, request normalization, author ownership, unchanged and revision-limit conflicts, clock-derived revision fields, legacy activity separation, separated revision and comment persistence, audit, watcher activity, response mapping, compatibility facade, unit-level facade caller, and scoped registration remain preserved.",
                "The work-item delete-comment route resolves DeleteCommentHandler directly; its route, authorization metadata, active-record and exact-comment lookup, author ownership, legacy activity separation, separated comment and revision deletion, in-memory response update, audit, watcher activity, response mapping, compatibility facade, API-flow facade caller, and scoped registration remain preserved.",
                "The work-item parent route resolves SetParentHandler directly; its route, authorization metadata, request binding, double-read project-structure lock, view and update authorization, hierarchy-level and board policy, completed-parent and cycle guards, unchanged-parent conflict, optimistic and separated persistence, audit, watcher activity, response mapping, compatibility facade, and scoped registration remain preserved.",
                "The work-item relation-create route resolves LinkWorkItemHandler directly; its route, authorization metadata, request binding, double-read project-structure lock, relation normalization, self-link, project, duplicate and graph-cycle guards, graph-before-document mutation order, optimistic and separated persistence, audit, watcher activity, response mapping, compatibility facade and unit callers, and scoped registration remain preserved.",
                "The work-item relation-delete route resolves UnlinkWorkItemHandler directly; its route and query binding, authorization metadata, double-read project-structure lock, relation normalization and exact removal, missing-relation contract, graph removal before persistence, optimistic and separated persistence, audit, watcher activity, response mapping, compatibility facade, and scoped registration remain preserved.",
                "The work-item attachment preview and download routes resolve OpenAttachmentHandler directly; their routes, authorization, active-record and repeated view checks, tenant hydration, legacy embedded fallback, separated attachment lookup, clean-scan gate, checksum-aware storage open, preview MIME rejection and stream disposal, cache, CSP, content disposition, range response behavior, compatibility facade, and scoped registration remain preserved.",
                "The work-item attachment-upload route resolves UploadAttachmentHandler directly; its route, multipart binding, stream lifetime, authorization, antiforgery and rate metadata, size and filename validation, lock, tenant authorization, legacy activity separation, attachment limit, storage save, exact document projection, separated persistence, storage compensation and logging, audit, watcher activity, response mapping, compatibility facade, and scoped registration remain preserved.",
                "The work-item attachment-delete route resolves DeleteAttachmentHandler directly; its route, binding, authorization, lock, tenant authorization, legacy activity separation, exact separated attachment lookup and deletion, in-memory projection update, storage deletion, persistence restore compensation and logging, exact document reconstruction, audit, watcher activity, response mapping, compatibility facade, and scoped registration remain preserved.",
                "The due-date reminder hosted service resolves SendDueDateRemindersHandler directly; enablement, interval, transaction boundary, cancellation and error handling, global and item locking, bounded candidate query, assignee authorization, stale-candidate rechecks, deduplication key, notification order, reminder timestamps, optimistic separated persistence, compatibility facade, and scoped registration remain preserved.",
                "The project-summary report route resolves ProjectSummaryHandler directly; its route, rate limit, report headers, view authorization, tenant scope, cache key and TTL, clock boundary, total, completed, in-progress and overdue filters, checked integer projection, snapshot mapping, compatibility facades, and scoped registration remain preserved.",
                "The status-distribution report route resolves StatusDistributionHandler directly; its route, rate limit, report headers, view authorization, active-item filter, cursor paging, cache key and TTL, ordinal status ordering, exact grouping and count projection, snapshot mapping, dashboard compatibility caller, public facades, and scoped registration remain preserved.",
                "The user-workload report route resolves UserWorkloadHandler directly; its route, rate limit, report headers, view authorization, organization-scoped activity read, active-item cursor paging, cache key and TTL, clock boundary, assignee filtering and ordering, open and overdue counts, activity-storage version fallback, logged-hours aggregation, snapshot mapping, dashboard compatibility caller, public facades, and scoped registration remain preserved.",
                "The due-date-risks report route resolves DueDateRisksHandler directly; its route, nullable days binding and default, rate limit, report headers, view authorization, days clamp, cache key and TTL, clock boundary, active incomplete due-date filter, cursor paging, due-date and ordinal ID ordering, response and snapshot mapping, dashboard and unit compatibility callers, public facades, and scoped registration remain preserved.",
                "The flow-time report route resolves FlowTimeHandler directly; its route, nullable date binding, rate limit, report headers, view authorization, default range and exact validation messages, UTC day boundaries, cache key and TTL, completed-item cursor paging, organization-scoped activity timeline and legacy fallback, reopened-work cycle start, lead and cycle samples, average and median rounding, response and snapshot mapping, dashboard and unit compatibility callers, public facades, and scoped registration remain preserved.",
                "The completion-rate report route resolves CompletionRateHandler directly; its route, nullable date binding, rate limit, report headers, view authorization, default range and exact validation messages, UTC day boundaries, cache key and TTL, created-item cursor paging, completion cutoff, empty-set behavior, two-decimal percentage, response and snapshot mapping, dashboard, API and unit compatibility callers, public facades, and scoped registration remain preserved.",
                "The team-performance report route resolves TeamPerformanceHandler directly; its route, nullable date binding, rate limit, report headers, view authorization, default range and exact validation messages, UTC day boundaries, cache key and TTL, team-policy call order, explicit team-assignment cursor paging, organization-scoped activity read and legacy fallback, team-name ordering, completion cutoff and percentage, average lead-time rounding, logged-hours aggregation, response and snapshot mapping, dashboard, API and unit compatibility callers, public facades, and scoped registration remain preserved.",
                "ListNotificationsHandler and MarkNotificationAsReadHandler remain scoped self-services; explicit factories select their port-focused constructors while compatibility constructors remain available.",
                "WriteAuditLogHandler and QueryAuditLogHandler remain scoped self-services; explicit factories select their port-focused constructors while compatibility constructors remain available.",
                "The capacity-plan archive route resolves ArchiveCapacityPlanHandler directly; its route, authorization, transaction filter, correlation ID, owner and visibility checks, optimistic concurrency, audit, response mapping, compatibility facade, and scoped registration remain preserved.",
                "The capacity-plan read route resolves GetCapacityPlanHandler directly; its route, authorization, transaction filter, archived binding, tenant and viewer masking, project visibility, response mapping, compatibility facade, and scoped registration remain preserved.",
                "The capacity-plan list route resolves ListCapacityPlansHandler through CapacityPlanAccessPolicy; its route, authorization, transaction filter, paging defaults and clamps, archived binding, tenant and viewer filtering, project visibility, ordering, response mapping, compatibility facade, and scoped registrations remain preserved.",
                "The capacity-plan sharing route resolves ShareCapacityPlanHandler through CapacityPlanAccessPolicy; its route, authorization, transaction filter, correlation ID, owner semantics, viewer normalization and limit, directory validation, optimistic concurrency, audit, response mapping, compatibility facade, and scoped registration remain preserved.",
                "The capacity-plan create and update routes resolve SaveCapacityPlanHandler; their routes, authorization, transaction filter, correlation IDs, request normalization, owner and tenant semantics, directory validation, optimistic concurrency, audit, response mapping, compatibility facade, and scoped registration remain preserved.",
                "The capacity-plan snapshot route resolves GetCapacitySnapshotHandler; its route, authorization, transaction filter, report rate limit, tenant and viewer masking, project visibility, source bounds, calculation formulas, response mapping, compatibility facade, and scoped registration remain preserved.",
                "The capacity-plan scenario route resolves PreviewScenarioHandler; its route, authorization, transaction filter, report rate limit, tenant and owner checks, allocation validation, shared source bounds and calculations, baseline/candidate response mapping, compatibility facade, and scoped registration remain preserved.",
                "The webhook subscription-list route resolves ListWebhookSubscriptionsHandler; its route, authorization, tenant resolution, manage permission, cursor traversal, response mapping, compatibility facade, and scoped registration remain preserved.",
                "The webhook subscription-read route resolves GetWebhookSubscriptionHandler; its route, authorization, tenant resolution, manage permission, ownership filter, not-found contract, response mapping, compatibility facade, and scoped registration remain preserved.",
                "The webhook delivery-metrics route resolves GetWebhookDeliveryMetricsHandler; its route, authorization, tenant resolution, manage permission, status counts, oldest-pending ordering, captured timestamp, response mapping, compatibility facade, and scoped registration remain preserved.",
                "The webhook delivery-list route resolves ListWebhookDeliveriesHandler; its route, authorization, tenant resolution, manage permission, subscription ownership, cursor normalization, page-size bounds, response mapping, compatibility facade, and scoped registration remain preserved.",
                "The webhook delivery-read route resolves GetWebhookDeliveryHandler; its route, authorization, tenant resolution, manage permission, ownership filter, not-found contract, response mapping, compatibility facade, and scoped registration remain preserved.",
                "The webhook delivery-replay route resolves ReplayWebhookDeliveryHandler; its route, authorization, rate limit, dead-letter filter, state reset, lease cleanup, optimistic replacement, audit, response mapping, compatibility facade, and scoped registration remain preserved.",
                "The webhook subscription enable and disable routes resolve SetSubscriptionStateHandler; their routes, authorization, requested state, expected version, optimistic replacement, audit actions and values, response mapping, compatibility facade, and scoped registration remain preserved.",
                "The webhook subscription-update route resolves UpdateSubscriptionHandler; its route, authorization, ownership filter, validation and normalization order, target policy, expected version, optimistic replacement, audit snapshots, response mapping, compatibility facade, and scoped registration remain preserved.",
                "The webhook subscription-create route resolves CreateSubscriptionHandler; its route, authorization, organization and user requirements, validation and normalization order, target policy, secret generation and protection, fingerprint, timestamps, audit, receipt mapping, compatibility facade, and scoped registration remain preserved.",
                "The webhook subscription secret-rotation route resolves RotateSecretHandler; its route, authorization, rate limit, ownership filter, secret generation and protection, previous-secret overlap, fingerprint and version updates, optimistic replacement, audit, receipt mapping, compatibility facade, and scoped registration remain preserved.",
                "The webhook test-delivery route resolves QueueTestDeliveryHandler; its route, authorization, rate limit, ownership and active-state checks, event identity, immutable payload and hash, pending delivery persistence, audit, response mapping, compatibility facade, and scoped registration remain preserved.",
                "QueueDeliveryHandler remains scoped and WorkItemWebhookDeliveryAdapter resolves it through an explicit factory while its original service constructor and WorkItemWebhookService.QueueAsync remain compatibility paths; scope filtering, subscription traversal, deterministic identity, payload schema, hashing, timestamps and idempotent conflict handling remain preserved.",
                "DispatchDeliveriesHandler remains scoped while WorkItemWebhookService.DispatchAsync delegates webhook delivery execution; due selection, lease claims, current and overlap signatures, sending, success finalization, retry jitter, dead-letter transitions, compatibility facade, and hosted-service contract remain preserved.",
                "The development provider-health route resolves CheckProviderHealthHandler directly; its route, policy, rate limit, correlation ID, response mapping, compatibility facade, and scoped registration remain preserved.",
                "The development repository-discovery route resolves ListRepositoriesHandler directly; its route, policy, rate limit, page projection, partial-result marker, compatibility facade, and scoped registration remain preserved.",
                "The development connection-list route resolves ListConnectionsHandler directly; its route, policy, response mapping, compatibility facade, and scoped registration remain preserved.",
                "The development connection-read route resolves GetConnectionHandler directly; its route, policy, tenant masking, response mapping, compatibility facade, and scoped registration remain preserved.",
                "The development connection-create route resolves CreateConnectionHandler directly; its route, policy, correlation ID, created response mapping, compatibility facade, and scoped registration remain preserved.",
                "The development credential-rotation route resolves RotateCredentialHandler directly; its route, policy, rate limit, correlation ID, response mapping, compatibility facade, and scoped registration remain preserved.",
                "The development webhook-secret-rotation route resolves RotateWebhookSecretHandler directly; its route, policy, rate limit, correlation ID, receipt mapping, previous-secret grace behavior, compatibility facade, and scoped registration remain preserved.",
                "The development disconnect route resolves DisconnectConnectionHandler directly; its route, policy, correlation ID, lifecycle cleanup, mapping deactivation, response mapping, compatibility facade, and scoped registration remain preserved.",
                "The development connection-delete route resolves DeleteConnectionHandler directly; its route, policy, expected-version binding, correlation ID, linked data cleanup, optimistic concurrency, audit, no-content response, compatibility facade, and scoped registration remain preserved.",
                "The development connection-mapping-list route resolves ListConnectionMappingsHandler directly; its route, policy, tenant masking, ordering, response mapping, compatibility facade, and scoped registration remain preserved.",
                "The development connection-mapping-create route resolves CreateMappingHandler directly; its route, policy, correlation ID, created response mapping, project authorization, compatibility facade, and scoped registration remain preserved.",
                "The development connection-mapping-delete route resolves DeleteMappingHandler directly; its route, policy, expected-version binding, correlation ID, no-content response, linked-data cleanup, compatibility facade, and scoped registration remain preserved.",
                "The development work-item-mapping route resolves ListWorkItemMappingsHandler directly; its route, transaction filter, WorkItemLink permission, tenant masking, active-project filtering, response mapping, compatibility facade, and scoped registration remain preserved.",
                "The development work-item-link-list route resolves ListWorkItemLinksHandler directly; its route, transaction filter, WorkItemView permission, tenant masking, connection-state projection, response mapping, compatibility facade, and scoped registration remain preserved.",
                "The development work-item-link-create route resolves CreateWorkItemLinkHandler directly; its route, transaction filter, WorkItemLink permission, correlation ID, validation, deterministic identity, idempotency, audit, created response mapping, compatibility facade, and scoped registration remain preserved.",
                "The development work-item-link-delete route resolves DeleteWorkItemLinkHandler directly; its route, transaction filter, WorkItemLink permission, expected-version binding, correlation ID, tenant masking, optimistic concurrency, audit, no-content response, compatibility facade, and scoped registration remain preserved.",
                "The development webhook ingress resolves ReceiveWebhookHandler directly after HTTP header and payload binding; its anonymous route, transaction filter, signature verification, previous-secret grace behavior, deduplication, collision detection, durable queueing, accepted response, compatibility facade, and scoped registration remain preserved.",
                "The development webhook durable adapter resolves ProcessWebhookHandler directly; receipt idempotency, connection lifecycle rejection, repository mapping, reference matching, deterministic link creation, stale-event protection, optimistic concurrency, audit behavior, compatibility facade, consumer name, event type, and scoped registration remain preserved."
            },
            intentionalConfigurationChanges = new[]
            {
                "Local Compose access tokens use an overridable 1440-minute demo lifetime; the base default remains 30 minutes.",
                "Local Compose Mongo commands use an overridable 300-second migration window; the base default remains 30 seconds.",
                "API dependency health timeout is configurable with a 5-second base default and a 30-second local Compose override.",
                "Gateway local upstream timeout changed from 30 to 60 seconds.",
                "Mongo, Redis, MinIO, and OpenSearch local health windows were lengthened.",
                "OpenSearch local retries/start period changed from 20/45s to 60/120s."
            }
        };
        return JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true })
            .Replace("\r\n", "\n", StringComparison.Ordinal) + "\n";
    }

    private static IReadOnlyList<string> EndpointContracts(
        IReadOnlyList<RefactorSourceReader.SourceFile> files) =>
        Parsed(files, file =>
                file.Path.StartsWith("Backend/src/Zumbo.Api/Endpoints/", StringComparison.Ordinal)
                || file.Path.StartsWith(
                    "Backend/src/Zumbo.Api/Presentation/Endpoints/",
                    StringComparison.Ordinal))
            .SelectMany(source => source.Root.DescendantNodes().OfType<InvocationExpressionSyntax>())
            .Where(invocation => HttpMapMethods.Contains(InvocationName(invocation)))
            .Select(invocation => Normalize(
                (SyntaxNode?)invocation.FirstAncestorOrSelf<StatementSyntax>() ?? invocation))
            .Order(StringComparer.Ordinal)
            .ToArray();

    private static IReadOnlyList<string> DiContracts(IReadOnlyList<RefactorSourceReader.SourceFile> files) =>
        Parsed(files, _ => true)
            .SelectMany(source => source.Root.DescendantNodes().OfType<InvocationExpressionSyntax>())
            .Where(invocation => DiMethods.Contains(InvocationName(invocation)))
            .Select(invocation => Normalize(
                (SyntaxNode?)invocation.FirstAncestorOrSelf<StatementSyntax>() ?? invocation))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();

    private static IReadOnlyList<string> MigrationContracts(
        IReadOnlyList<RefactorSourceReader.SourceFile> files)
    {
        var roots = Parsed(files, file =>
                file.Path.StartsWith("Backend/src/Zumbo.Persistence.PostgreSql/PostgreSqlMigrations", StringComparison.Ordinal))
            .ToArray();
        var migrationInvocations = roots
            .SelectMany(source => source.Root.DescendantNodes().OfType<InvocationExpressionSyntax>())
            .Where(invocation => InvocationName(invocation) == "Create"
                && invocation.Expression.ToString().Contains("Migration", StringComparison.Ordinal))
            .ToArray();
        var referencedSqlNames = migrationInvocations
            .SelectMany(invocation => invocation.ArgumentList.Arguments.Skip(2).Select(argument => argument.Expression.ToString()))
            .ToHashSet(StringComparer.Ordinal);
        var initializers = roots
            .SelectMany(source => source.Root.DescendantNodes().OfType<VariableDeclaratorSyntax>())
            .Where(variable => variable.Initializer is not null
                && referencedSqlNames.Contains(variable.Identifier.ValueText))
            .GroupBy(variable => variable.Identifier.ValueText, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.Select(variable => Normalize(variable.Initializer!.Value))
                    .Distinct(StringComparer.Ordinal)
                    .Single(),
                StringComparer.Ordinal);

        return migrationInvocations
            .Select(invocation =>
            {
                var arguments = invocation.ArgumentList.Arguments;
                Assert.Equal(4, arguments.Count);
                var upName = arguments[2].Expression.ToString();
                var downName = arguments[3].Expression.ToString();
                return string.Join('|',
                    Normalize(arguments[0].Expression),
                    Normalize(arguments[1].Expression),
                    upName,
                    initializers[upName],
                    downName,
                    initializers[downName]);
            })
            .OrderBy(item => long.Parse(item[..item.IndexOf('|')]))
            .ToArray();
    }

    private static IReadOnlyList<string> MongoContracts(
        IReadOnlyList<RefactorSourceReader.SourceFile> files) =>
        Parsed(files, _ => true)
            .SelectMany(source => source.Root.DescendantNodes())
            .Where(node => node switch
            {
                ObjectCreationExpressionSyntax creation =>
                    creation.Type.ToString().Contains("CreateIndexModel", StringComparison.Ordinal),
                InvocationExpressionSyntax invocation => InvocationName(invocation) is
                    "GetCollection" or "CreateOneAsync" or "CreateManyAsync" or "Indexes",
                _ => false
            })
            .Select(Normalize)
            .Order(StringComparer.Ordinal)
            .ToArray();

    private static IReadOnlyList<string> SerializationContracts(
        IReadOnlyList<RefactorSourceReader.SourceFile> files) =>
        Parsed(files, _ => true)
            .SelectMany(source => source.Root.DescendantNodes().OfType<AttributeSyntax>())
            .Where(attribute => SerializationAttributeName(attribute) is
                "JsonPropertyName" or "BsonElement" or "BsonDiscriminator" or "BsonKnownTypes")
            .Select(Normalize)
            .Order(StringComparer.Ordinal)
            .ToArray();

    private static IReadOnlyList<string> MessagingContracts(
        IReadOnlyList<RefactorSourceReader.SourceFile> files) =>
        Parsed(files, _ => true)
            .SelectMany(source => source.Root.DescendantNodes().OfType<TypeDeclarationSyntax>())
            .Where(type => MessagingType(type.Identifier.ValueText))
            .SelectMany(type => type.Members
                .Where(member => member is not BaseTypeDeclarationSyntax)
                .Select(member => $"{QualifiedTypeName(type)}|{Normalize(member)}"))
            .Order(StringComparer.Ordinal)
            .ToArray();

    private static bool MessagingType(string name) =>
        !name.EndsWith("Endpoint", StringComparison.Ordinal)
        && (name.Contains("Event", StringComparison.Ordinal)
            || name.Contains("Message", StringComparison.Ordinal)
            || name.Contains("Inbox", StringComparison.Ordinal)
            || name.Contains("Outbox", StringComparison.Ordinal)
            || name.Contains("DeadLetter", StringComparison.Ordinal));

    private static string QualifiedTypeName(TypeDeclarationSyntax type)
    {
        var namespaceName = string.Join(".", type.Ancestors()
            .OfType<BaseNamespaceDeclarationSyntax>()
            .Reverse()
            .Select(item => item.Name.ToString()));
        return string.IsNullOrEmpty(namespaceName)
            ? type.Identifier.ValueText
            : $"{namespaceName}.{type.Identifier.ValueText}";
    }

    private static void AssertConfigurationChangesAreIntentionalAndBounded()
    {
        var baselineSettings = RefactorSourceReader.ReadGitFile(
            RepositoryDirectory,
            RefactorSemanticInventory.BaselineCommit,
            "Backend/src/Zumbo.Api/appsettings.json");
        var targetSettings = File.ReadAllText(Path.Combine(
            ProjectDirectory,
            "Backend", "src", "Zumbo.Api", "appsettings.json"));
        var baselineLeaves = FlattenJson(baselineSettings);
        var targetLeaves = FlattenJson(targetSettings);

        Assert.All(baselineLeaves, item => Assert.Equal(item.Value, targetLeaves[item.Key]));
        var addedSettings = targetLeaves.Keys.Except(baselineLeaves.Keys, StringComparer.Ordinal).ToArray();
        Assert.Equal(["HealthChecks:DependencyTimeoutSeconds"], addedSettings);
        Assert.Equal("5", targetLeaves["HealthChecks:DependencyTimeoutSeconds"]);

        var baselineCompose = RefactorSourceReader.ReadGitFile(
            RepositoryDirectory,
            RefactorSemanticInventory.BaselineCommit,
            "Backend/docker-compose.yml");
        var targetCompose = File.ReadAllText(Path.Combine(ProjectDirectory, "Backend", "docker-compose.yml"));
        var difference = LineMultisetDifference(baselineCompose, targetCompose);

        Assert.Equal(
        [
            "Gateway__UpstreamTimeoutSeconds: 30",
            "retries: 20",
            "start_period: 45s",
            "timeout: 3s",
            "timeout: 5s",
            "timeout: 5s",
            "timeout: 5s"
        ], difference.Removed);
        Assert.Equal(
        [
            "Gateway__UpstreamTimeoutSeconds: 60",
            "HealthChecks__DependencyTimeoutSeconds: 30",
            "HealthChecks__DependencyTimeoutSeconds: 30",
            "Jwt__AccessTokenMinutes: ${ZUMBO_ACCESS_TOKEN_MINUTES:-1440}",
            "Jwt__AccessTokenMinutes: ${ZUMBO_ACCESS_TOKEN_MINUTES:-1440}",
            "MongoDb__CommandTimeoutSeconds: ${ZUMBO_MONGO_COMMAND_TIMEOUT_SECONDS:-300}",
            "MongoDb__CommandTimeoutSeconds: ${ZUMBO_MONGO_COMMAND_TIMEOUT_SECONDS:-300}",
            "retries: 60",
            "start_period: 120s",
            "timeout: 15s",
            "timeout: 20s",
            "timeout: 20s",
            "timeout: 20s"
        ], difference.Added);
    }

    private static IReadOnlyDictionary<string, string> FlattenJson(string json)
    {
        using var document = JsonDocument.Parse(json);
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        AddJsonLeaves(document.RootElement, string.Empty, result);
        return result;
    }

    private static void AddJsonLeaves(
        JsonElement element,
        string path,
        IDictionary<string, string> result)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                AddJsonLeaves(property.Value, path.Length == 0 ? property.Name : $"{path}:{property.Name}", result);
            }
            return;
        }
        if (element.ValueKind == JsonValueKind.Array)
        {
            var index = 0;
            foreach (var item in element.EnumerateArray())
            {
                AddJsonLeaves(item, $"{path}:{index++}", result);
            }
            return;
        }
        result[path] = element.GetRawText();
    }

    private static LineDifference LineMultisetDifference(string baseline, string target)
    {
        var baselineLines = Lines(baseline).ToList();
        var targetLines = Lines(target).ToList();
        foreach (var line in baselineLines.ToArray())
        {
            var targetIndex = targetLines.IndexOf(line);
            if (targetIndex < 0)
            {
                continue;
            }
            baselineLines.Remove(line);
            targetLines.RemoveAt(targetIndex);
        }
        return new LineDifference(
            baselineLines.Order(StringComparer.Ordinal).ToArray(),
            targetLines.Order(StringComparer.Ordinal).ToArray());
    }

    private static IEnumerable<string> Lines(string value) => value
        .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
        .Select(line => line.Trim())
        .Where(line => line.Length > 0);

    private static IEnumerable<ParsedSource> Parsed(
        IReadOnlyList<RefactorSourceReader.SourceFile> files,
        Func<RefactorSourceReader.SourceFile, bool> predicate) =>
        files.Where(predicate).Select(file => new ParsedSource(
            file,
            CSharpSyntaxTree.ParseText(
                    file.Content,
                    CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Latest))
                .GetCompilationUnitRoot()));

    private static string InvocationName(InvocationExpressionSyntax invocation) =>
        invocation.Expression switch
        {
            MemberAccessExpressionSyntax member => member.Name.Identifier.ValueText,
            IdentifierNameSyntax identifier => identifier.Identifier.ValueText,
            GenericNameSyntax generic => generic.Identifier.ValueText,
            _ => string.Empty
        };

    private static string SerializationAttributeName(AttributeSyntax attribute) =>
        attribute.Name.ToString().Split('.').Last().Replace("Attribute", string.Empty, StringComparison.Ordinal);

    private static string Normalize(SyntaxNode node) =>
        string.Concat(node.DescendantTokens().Select(token => token.Text));

    private static void AssertExact(string contract, IReadOnlyList<string> baseline, IReadOnlyList<string> target)
    {
        var difference = MultisetDifference(baseline, target);
        Assert.True(
            difference.Removed.Count == 0 && difference.Added.Count == 0,
            $"{contract} changed. Baseline={baseline.Count}, target={target.Count}. "
            + $"Missing=[{string.Join(", ", difference.Removed.Take(3))}] "
            + $"Added=[{string.Join(", ", difference.Added.Take(3))}]");
    }

    private static void AssertExactWithAllowedAdditions(
        string contract,
        IReadOnlyList<string> baseline,
        IReadOnlyList<string> target,
        IReadOnlyList<string> allowedAdditions)
    {
        var difference = MultisetDifference(baseline, target);
        var unexplained = MultisetDifference(allowedAdditions, difference.Added);
        Assert.True(
            difference.Removed.Count == 0
            && unexplained.Removed.Count == 0
            && unexplained.Added.Count == 0,
            $"{contract} changed outside the accepted additions. "
            + $"Missing=[{string.Join(", ", difference.Removed.Take(3))}] "
            + $"Unexpected=[{string.Join(", ", unexplained.Added.Take(3))}] "
                + $"UnobservedAccepted=[{string.Join(", ", unexplained.Removed.Take(3))}]");
    }

    private static void AssertExactWithAllowedReplacements(
        string contract,
        IReadOnlyList<string> baseline,
        IReadOnlyList<string> target,
        IReadOnlyList<string> allowedRemoved,
        IReadOnlyList<string> allowedAdded)
    {
        var difference = MultisetDifference(baseline, target);
        var unexplainedRemoved = MultisetDifference(allowedRemoved, difference.Removed);
        var unexplainedAdded = MultisetDifference(allowedAdded, difference.Added);
        Assert.True(
            unexplainedRemoved.Removed.Count == 0
            && unexplainedRemoved.Added.Count == 0
            && unexplainedAdded.Removed.Count == 0
            && unexplainedAdded.Added.Count == 0,
            $"{contract} changed outside the accepted replacements. "
                + $"Missing=[{string.Join(", ", unexplainedRemoved.Added.Take(3))}] "
                + $"Unexpected=[{string.Join(", ", unexplainedAdded.Added.Take(3))}] "
                + $"UnobservedRemoved=[{string.Join(", ", unexplainedRemoved.Removed.Take(3))}] "
                + $"UnobservedAdded=[{string.Join(", ", unexplainedAdded.Removed.Take(3))}]");
    }

    private static LineDifference MultisetDifference(
        IEnumerable<string> baseline,
        IEnumerable<string> target)
    {
        var baselineItems = baseline.ToList();
        var targetItems = target.ToList();
        foreach (var item in baselineItems.ToArray())
        {
            var targetIndex = targetItems.IndexOf(item);
            if (targetIndex < 0)
            {
                continue;
            }
            baselineItems.Remove(item);
            targetItems.RemoveAt(targetIndex);
        }
        return new LineDifference(baselineItems, targetItems);
    }

    private sealed record ParsedSource(
        RefactorSourceReader.SourceFile File,
        CompilationUnitSyntax Root);

    private sealed record LineDifference(
        IReadOnlyList<string> Removed,
        IReadOnlyList<string> Added);
}
