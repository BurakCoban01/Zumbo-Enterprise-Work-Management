using Microsoft.Extensions.Options;
using Zumbo.Api.Composition.Modules.WorkItems;
using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.Modules.WorkItems;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Search;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.Modules.WorkItems.Application.Features.BulkOperations.Archive;
using Zumbo.Modules.WorkItems.Application.Features.BulkOperations.Assign;
using Zumbo.Modules.WorkItems.Application.Features.BulkOperations.Move;
using Zumbo.SharedKernel;

using static ApiEndpointResults;

internal static partial class WorkItemEndpoints
{
    internal static IServiceCollection AddWorkItemsModule(
        this IServiceCollection services,
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
        services.AddScoped<IWorkItemTypeSchemaPolicy>(provider =>
            provider.GetRequiredService<WorkItemTypeSchemaService>());
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
