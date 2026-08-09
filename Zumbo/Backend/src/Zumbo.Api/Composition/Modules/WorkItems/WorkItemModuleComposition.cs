using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Search;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.Modules.WorkItems;
using Zumbo.Modules.WorkItems.Application.Features.Recurrences;
using Zumbo.Modules.WorkItems.Application.Features.Schema;
using Zumbo.SharedKernel;

namespace Zumbo.Api.Composition.Modules.WorkItems;

internal static class WorkItemModuleComposition
{
    internal static IServiceCollection AddWorkItemsModule(
        IServiceCollection services,
        IConfiguration? configuration = null)
    {
        services.AddScoped<IProjectPermissionChecker, ProjectPermissionCheckerAdapter>();
        services.AddScoped<IWorkItemTeamPolicy, WorkItemTeamPolicyAdapter>();
        services.AddScoped<IWorkflowPolicy, WorkflowPolicyAdapter>();
        services.AddScoped<IBoardPlacementPolicy>(provider => provider.GetRequiredService<BoardPolicyAdapter>());
        services.AddScoped<WorkItemWipProjection>();
        services.AddOptions<WorkItemRankOptions>()
            .BindConfiguration("WorkItemRank")
            .Validate(
                options => options.BatchSize is >= 1 and <= 200
                    && options.MaxBatchesPerRun is >= 4 and <= 10_000,
                "WorkItemRank batch settings are outside the supported bounds.")
            .ValidateOnStart();
        services.AddScoped<WorkItemRankService>();
        services.AddOptions<WorkItemGraphOptions>()
            .BindConfiguration("WorkItemGraph")
            .Validate(
                options => options.MaxTraversalDepth is >= 1 and <= 256
                    && options.MaxVisitedNodes is >= 10 and <= 10_000
                    && options.MaxOutgoingDependenciesPerNode is >= 1 and <= 200
                    && options.MaxRelationsPerWorkItem is >= 1 and <= 1_000
                    && options.MaxChildrenPerWorkItem is >= 1 and <= 1_000,
                "WorkItemGraph limits are outside the supported bounds.")
            .ValidateOnStart();
        services.AddScoped<WorkItemGraphService>();
        services.AddScoped<IWorkItemCollaboratorDirectory, WorkItemCollaboratorDirectoryAdapter>();
        services.AddScoped<WorkItemCollaborationService>();
        services.AddOptions<WorkItemRecurrenceOptions>()
            .BindConfiguration("WorkItemRecurrence")
            .Validate(
                options => options.IntervalSeconds is >= 5 and <= 3600
                    && options.BatchSize is >= 1 and <= 200
                    && options.MaximumOccurrences is >= 1 and <= 10_000
                    && options.MaximumScheduleYears is >= 1 and <= 20,
                "Work-item recurrence settings are outside the supported bounds.")
            .ValidateOnStart();
        services.AddScoped<WorkItemTemplateRecurrenceService>();
        services.AddScoped<ListWorkItemTemplatesHandler>(provider => new ListWorkItemTemplatesHandler(
            provider.GetRequiredService<IDocumentRepository<WorkItemTemplateDocument>>(),
            provider.GetRequiredService<IProjectPermissionChecker>(),
            provider.GetRequiredService<ICurrentUser>()));
        services.AddScoped<ListWorkItemRecurrencesHandler>(provider => new ListWorkItemRecurrencesHandler(
            provider.GetRequiredService<IDocumentRepository<WorkItemRecurrenceDocument>>(),
            provider.GetRequiredService<IDocumentRepository<WorkItemRecurrenceOccurrenceDocument>>(),
            provider.GetRequiredService<IProjectPermissionChecker>(),
            provider.GetRequiredService<ICurrentUser>()));
        services.AddScoped<ListRecurrenceOccurrencesHandler>(provider => new ListRecurrenceOccurrencesHandler(
            provider.GetRequiredService<IDocumentRepository<WorkItemRecurrenceDocument>>(),
            provider.GetRequiredService<IDocumentRepository<WorkItemRecurrenceOccurrenceDocument>>(),
            provider.GetRequiredService<IProjectPermissionChecker>(),
            provider.GetRequiredService<ICurrentUser>()));
        services.AddScoped<PreviewWorkItemRecurrenceHandler>(provider => new PreviewWorkItemRecurrenceHandler(
            provider.GetRequiredService<IDocumentRepository<WorkItemTemplateDocument>>(),
            provider.GetRequiredService<IProjectPermissionChecker>(),
            provider.GetRequiredService<ICurrentUser>(),
            provider.GetRequiredService<IOptions<WorkItemRecurrenceOptions>>(),
            provider.GetRequiredService<IClock>()));
        services.AddScoped<CreateWorkItemRecurrenceHandler>(provider => new CreateWorkItemRecurrenceHandler(
            provider.GetRequiredService<IDocumentRepository<WorkItemTemplateDocument>>(),
            provider.GetRequiredService<IDocumentRepository<WorkItemRecurrenceDocument>>(),
            provider.GetRequiredService<IDocumentRepository<WorkItemRecurrenceOccurrenceDocument>>(),
            provider.GetRequiredService<IProjectPermissionChecker>(),
            provider.GetRequiredService<ICurrentUser>(),
            provider.GetRequiredService<IOptions<WorkItemRecurrenceOptions>>(),
            provider.GetRequiredService<IClock>(),
            provider.GetRequiredService<IWorkItemAuditPublisher>()));
        services.AddScoped<SetWorkItemRecurrenceStateHandler>(provider => new SetWorkItemRecurrenceStateHandler(
            provider.GetRequiredService<IDocumentRepository<WorkItemRecurrenceDocument>>(),
            provider.GetRequiredService<IDocumentRepository<WorkItemRecurrenceOccurrenceDocument>>(),
            provider.GetRequiredService<IProjectPermissionChecker>(),
            provider.GetRequiredService<ICurrentUser>(),
            provider.GetRequiredService<IDistributedLockProvider>(),
            provider.GetRequiredService<IOptions<DistributedLockOptions>>(),
            provider.GetRequiredService<IClock>(),
            provider.GetRequiredService<IWorkItemAuditPublisher>(),
            provider.GetService<IExpectedVersionAccessor>()));
        services.AddScoped<ArchiveWorkItemRecurrenceHandler>(provider => new ArchiveWorkItemRecurrenceHandler(
            provider.GetRequiredService<IDocumentRepository<WorkItemRecurrenceDocument>>(),
            provider.GetRequiredService<IProjectPermissionChecker>(),
            provider.GetRequiredService<ICurrentUser>(),
            provider.GetRequiredService<IDistributedLockProvider>(),
            provider.GetRequiredService<IOptions<DistributedLockOptions>>(),
            provider.GetRequiredService<IClock>(),
            provider.GetRequiredService<IWorkItemAuditPublisher>(),
            provider.GetService<IExpectedVersionAccessor>()));
        services.AddScoped<CreateWorkItemTemplateHandler>(provider => new CreateWorkItemTemplateHandler(
            provider.GetRequiredService<IDocumentRepository<WorkItemTemplateDocument>>(),
            provider.GetRequiredService<IProjectPermissionChecker>(),
            provider.GetRequiredService<ICurrentUser>(),
            provider.GetRequiredService<IWorkItemTeamPolicy>(),
            provider.GetRequiredService<IWorkItemCollaboratorDirectory>(),
            provider.GetRequiredService<IBoardPlacementPolicy>(),
            provider.GetRequiredService<IWorkItemTypeSchemaPolicy>(),
            provider.GetRequiredService<IDistributedLockProvider>(),
            provider.GetRequiredService<IOptions<DistributedLockOptions>>(),
            provider.GetRequiredService<IClock>(),
            provider.GetRequiredService<IWorkItemAuditPublisher>()));
        services.AddScoped<UpdateWorkItemTemplateHandler>(provider => new UpdateWorkItemTemplateHandler(
            provider.GetRequiredService<IDocumentRepository<WorkItemTemplateDocument>>(),
            provider.GetRequiredService<IDocumentRepository<WorkItemRecurrenceDocument>>(),
            provider.GetRequiredService<IProjectPermissionChecker>(),
            provider.GetRequiredService<ICurrentUser>(),
            provider.GetRequiredService<IWorkItemTeamPolicy>(),
            provider.GetRequiredService<IWorkItemCollaboratorDirectory>(),
            provider.GetRequiredService<IBoardPlacementPolicy>(),
            provider.GetRequiredService<IWorkItemTypeSchemaPolicy>(),
            provider.GetRequiredService<IDistributedLockProvider>(),
            provider.GetRequiredService<IOptions<DistributedLockOptions>>(),
            provider.GetRequiredService<IClock>(),
            provider.GetRequiredService<IWorkItemAuditPublisher>(),
            provider.GetService<IExpectedVersionAccessor>()));
        services.AddScoped<ArchiveWorkItemTemplateHandler>(provider => new ArchiveWorkItemTemplateHandler(
            provider.GetRequiredService<IDocumentRepository<WorkItemTemplateDocument>>(),
            provider.GetRequiredService<IDocumentRepository<WorkItemRecurrenceDocument>>(),
            provider.GetRequiredService<IProjectPermissionChecker>(),
            provider.GetRequiredService<ICurrentUser>(),
            provider.GetRequiredService<IDistributedLockProvider>(),
            provider.GetRequiredService<IOptions<DistributedLockOptions>>(),
            provider.GetRequiredService<IClock>(),
            provider.GetRequiredService<IWorkItemAuditPublisher>(),
            provider.GetService<IExpectedVersionAccessor>()));
        services.AddScoped<ScheduleDueRecurrencesHandler>(provider => new ScheduleDueRecurrencesHandler(
            provider.GetRequiredService<IDocumentRepository<WorkItemTemplateDocument>>(),
            provider.GetRequiredService<IDocumentRepository<WorkItemRecurrenceDocument>>(),
            provider.GetRequiredService<IDocumentRepository<WorkItemRecurrenceOccurrenceDocument>>(),
            provider.GetRequiredService<IWorkItemRecurrenceEventPublisher>(),
            provider.GetRequiredService<IDistributedLockProvider>(),
            provider.GetRequiredService<IOptions<DistributedLockOptions>>(),
            provider.GetRequiredService<IOptions<WorkItemRecurrenceOptions>>(),
            provider.GetRequiredService<IClock>()));
        services.AddScoped<RecurringWorkItemGenerator>();
        services.AddWorkItemBulkOperations();
        services.AddScoped<SearchMaintenanceService>();
        services.AddOptions<WorkItemTypeSchemaOptions>()
            .BindConfiguration("WorkItemTypeSchema")
            .Validate(
                options => options.BatchSize is >= 1 and <= 200
                    && options.MaxBatchesPerValidation is >= 1 and <= 10_000,
                "WorkItemTypeSchema batch settings are outside the supported bounds.")
            .ValidateOnStart();
        services.AddScoped<WorkItemTypeSchemaService>();
        services.AddScoped<GetWorkItemTypeSchemaHandler>(provider => new GetWorkItemTypeSchemaHandler(
            provider.GetRequiredService<IDocumentRepository<WorkItemTypeSchemaDocument>>(),
            provider.GetRequiredService<IDocumentRepository<WorkItemDocument>>(),
            provider.GetRequiredService<IProjectPermissionChecker>(),
            provider.GetRequiredService<ICurrentUser>(),
            provider.GetRequiredService<IOptions<WorkItemTypeSchemaOptions>>(),
            provider.GetRequiredService<IClock>()));
        services.AddScoped<GetIssueTypeDistributionHandler>(provider => new GetIssueTypeDistributionHandler(
            provider.GetRequiredService<IDocumentRepository<WorkItemTypeSchemaDocument>>(),
            provider.GetRequiredService<IDocumentRepository<WorkItemDocument>>(),
            provider.GetRequiredService<IProjectPermissionChecker>(),
            provider.GetRequiredService<ICurrentUser>(),
            provider.GetRequiredService<IOptions<WorkItemTypeSchemaOptions>>(),
            provider.GetRequiredService<IClock>()));
        services.AddScoped<GetCustomFieldDistributionHandler>(provider => new GetCustomFieldDistributionHandler(
            provider.GetRequiredService<IDocumentRepository<WorkItemTypeSchemaDocument>>(),
            provider.GetRequiredService<IDocumentRepository<WorkItemDocument>>(),
            provider.GetRequiredService<IProjectPermissionChecker>(),
            provider.GetRequiredService<ICurrentUser>(),
            provider.GetRequiredService<IOptions<WorkItemTypeSchemaOptions>>(),
            provider.GetRequiredService<IClock>()));
        services.AddScoped<UpsertWorkItemTypeSchemaHandler>(provider => new UpsertWorkItemTypeSchemaHandler(
            provider.GetRequiredService<IDocumentRepository<WorkItemTypeSchemaDocument>>(),
            provider.GetRequiredService<IDocumentRepository<WorkItemDocument>>(),
            provider.GetRequiredService<IProjectPermissionChecker>(),
            provider.GetRequiredService<IWorkItemAuditPublisher>(),
            provider.GetRequiredService<IDistributedLockProvider>(),
            provider.GetRequiredService<IOptions<DistributedLockOptions>>(),
            provider.GetRequiredService<IOptions<WorkItemTypeSchemaOptions>>(),
            provider.GetRequiredService<IClock>(),
            provider.GetRequiredService<ICurrentUser>(),
            provider.GetService<IExpectedVersionAccessor>()));
        services.AddScoped<ValidateWorkItemShapeHandler>(provider => new ValidateWorkItemShapeHandler(
            provider.GetRequiredService<IDocumentRepository<WorkItemTypeSchemaDocument>>(),
            provider.GetRequiredService<IClock>()));
        services.AddScoped<GetIssueTypeHierarchyHandler>(provider => new GetIssueTypeHierarchyHandler(
            provider.GetRequiredService<IDocumentRepository<WorkItemTypeSchemaDocument>>(),
            provider.GetRequiredService<IClock>()));
        services.AddScoped<ValidateWorkItemSearchFilterHandler>(provider =>
            new ValidateWorkItemSearchFilterHandler(
                provider.GetRequiredService<IDocumentRepository<WorkItemTypeSchemaDocument>>(),
                provider.GetRequiredService<IClock>()));
        services.AddScoped<IWorkItemTypeSchemaPolicy, WorkItemTypeSchemaPolicyAdapter>();
        services.AddScoped<IAttachmentStorage, AttachmentStorageAdapter>();
        services.AddScoped<AttachmentSecurityMaintenanceService>();
        services.AddScoped<OperationsStorageSecurityCoordinator>();
        services.AddScoped<IWorkItemOperationsAuditWriter, WorkItemOperationsAuditWriterAdapter>();
        services.AddWorkItemPublicationServices();
        services.AddWorkItemWebhookServices();
        services.AddDevelopmentIntegrationServices();
        services.AddWorkItemDurableEventHandlers();
        services.AddScoped<WorkItemTransactionFilter>();
        services.AddScoped<IWorkItemActivityStore, WorkItemActivityStore>();
        services.AddScoped<WorkItemActivityQueryService>();
        services.AddScoped<WorkItemService>();
        services.AddWorkItemIntakeServices();
        services.AddWorkItemCoreCreateAndReadHandlers();
        services.AddWorkItemChecklistHandlers();
        services.AddWorkItemWorklogHandlers();
        services.AddWorkItemPlanningHandlers();
        services.AddWorkItemCommentHandlers();
        services.AddWorkItemRelationHandlers();
        services.AddWorkItemAttachmentHandlers();
        services.AddWorkItemReminderHandlers();
        services.AddWorkItemReportHandlers();
        services.AddWorkItemAssignmentHandlers();
        services.AddWorkItemCustomFieldsHandlers();
        services.AddWorkItemApprovalRequestHandler();
        services.AddWorkItemCoreUpdateHandler();
        services.AddWorkItemApprovalDecisionHandler();
        services.AddWorkItemBackgroundServices(configuration);
        return services;
    }
}
