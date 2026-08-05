using Microsoft.Extensions.Options;
using Zumbo.Api.Infrastructure.BackgroundServices.Webhooks;
using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.Modules.WorkItems;
using Zumbo.BuildingBlocks.Application.Messaging;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Search;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.Modules.WorkItems.Application.Features.Development.Connections;
using Zumbo.Modules.WorkItems.Application.Features.Development.Credentials;
using Zumbo.Modules.WorkItems.Application.Features.Development.ProviderHealth;
using Zumbo.Modules.WorkItems.Application.Features.Development.Repositories;
using Zumbo.Modules.WorkItems.Application.Features.Development.Mappings;
using Zumbo.Modules.WorkItems.Application.Features.Development.Links;
using Zumbo.Modules.WorkItems.Application.Features.Development.Webhooks;
using Zumbo.Modules.WorkItems.Application.Features.Webhooks.Deliveries;
using Zumbo.Modules.WorkItems.Application.Features.Webhooks.Subscriptions;
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
        services.AddOptions<WorkItemBulkJobOptions>()
            .BindConfiguration("WorkItemBulkJobs")
            .Validate(
                options => options.BatchSize is >= 1 and <= 200
                    && options.MaxInputItems is >= 1 and <= 10_000
                    && options.MaxInputBytes is >= 1_024 and <= 50 * 1024 * 1024
                    && options.MaxExportItems is >= 1 and <= 100_000
                    && options.MaxArtifactBytes is >= 1_024 and <= 100 * 1024 * 1024
                    && options.ArtifactRetentionDays is >= 1 and <= 90,
                "Work-item bulk job limits are outside the supported bounds.")
            .ValidateOnStart();
        services.AddScoped<IWorkItemBulkArtifactStorage, WorkItemBulkArtifactStorageAdapter>();
        services.AddScoped<WorkItemBulkJobService>();
        services.AddScoped<WorkItemBulkJobProcessor>();
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
        services.AddScoped<SignalRWorkItemRealtimePublisher>();
        services.AddScoped<DurableWorkItemEventPublisher>();
        services.AddScoped<IWorkItemAuditPublisher>(provider => provider.GetRequiredService<DurableWorkItemEventPublisher>());
        services.AddScoped<IWorkItemNotificationPublisher>(provider => provider.GetRequiredService<DurableWorkItemEventPublisher>());
        services.AddScoped<IWorkItemSearchPublisher>(provider => provider.GetRequiredService<DurableWorkItemEventPublisher>());
        services.AddScoped<IWorkItemRealtimePublisher>(provider => provider.GetRequiredService<DurableWorkItemEventPublisher>());
        services.AddScoped<IWorkItemCacheInvalidationPublisher>(provider => provider.GetRequiredService<DurableWorkItemEventPublisher>());
        services.AddScoped<IWorkItemRecurrenceEventPublisher>(provider => provider.GetRequiredService<DurableWorkItemEventPublisher>());
        services.AddScoped<IWorkItemBulkJobEventPublisher>(provider => provider.GetRequiredService<DurableWorkItemEventPublisher>());
        services.AddScoped<IWorkItemAutomationEventPublisher>(provider => provider.GetRequiredService<DurableWorkItemEventPublisher>());
        services.AddScoped<IDevelopmentWebhookQueue>(provider => provider.GetRequiredService<DurableWorkItemEventPublisher>());
        services.AddScoped<IWorkItemAutomationChainContextAccessor, WorkItemAutomationChainContextAccessor>();
        services.AddOptions<WebhookOptions>()
            .BindConfiguration("Webhooks")
            .Validate(options => options.MaximumAttempts is >= 1 and <= 20
                && options.BaseRetrySeconds is >= 1 and <= 3600
                && options.MaximumRetrySeconds >= options.BaseRetrySeconds
                && options.MaximumRetrySeconds <= 86400
                && options.RetryJitterRatio is >= 0 and <= 1
                && options.LeaseSeconds is >= 5 and <= 900
                && options.RequestTimeoutSeconds is >= 1 and <= 30
                && options.DispatchBatchSize is >= 1 and <= 100
                && options.DispatcherIntervalSeconds is >= 1 and <= 3600
                && options.RotationOverlapMinutes is >= 1 and <= 1440,
                "Webhook delivery configuration is invalid.")
            .ValidateOnStart();
        services.AddSingleton<WebhookTargetPolicy>();
        services.AddSingleton<IWebhookTargetPolicy>(provider => provider.GetRequiredService<WebhookTargetPolicy>());
        services.AddSingleton<IWebhookSecretProtector, WebhookSecretProtectorAdapter>();
        services.AddSingleton<IWebhookSender, PinnedWebhookSender>();
        services.AddScoped<IWebhookAuthorization, WebhookAuthorizationAdapter>();
        services.AddScoped<ListWebhookSubscriptionsHandler>();
        services.AddScoped<GetWebhookSubscriptionHandler>();
        services.AddScoped<GetWebhookDeliveryMetricsHandler>();
        services.AddScoped<ListWebhookDeliveriesHandler>();
        services.AddScoped<GetWebhookDeliveryHandler>();
        services.AddScoped<ReplayWebhookDeliveryHandler>();
        services.AddScoped<SetSubscriptionStateHandler>();
        services.AddScoped<UpdateSubscriptionHandler>();
        services.AddScoped<CreateSubscriptionHandler>();
        services.AddScoped<RotateSecretHandler>();
        services.AddScoped<QueueTestDeliveryHandler>();
        services.AddScoped<QueueDeliveryHandler>();
        services.AddScoped<DispatchDeliveriesHandler>();
        services.AddScoped<WorkItemWebhookService>();
        services.AddScoped<IWorkItemWebhookDelivery>(provider =>
            new WorkItemWebhookDeliveryAdapter(
                provider.GetRequiredService<QueueDeliveryHandler>()));
        services.AddOptions<DevelopmentProviderOptions>()
            .BindConfiguration("DevelopmentProviders")
            .Validate(
                options => options.RequestTimeoutSeconds is >= 1 and <= 30
                    && options.MaximumResponseBytes is >= 1_024 and <= 8 * 1_024 * 1_024
                    && options.AllowedHosts.Length is >= 1 and <= 100
                    && options.AllowedHosts.All(host =>
                        !string.IsNullOrWhiteSpace(host)
                        && host.Length <= 253
                        && !host.Contains('*')),
                "Development provider configuration is invalid.")
            .ValidateOnStart();
        services.AddSingleton<DevelopmentProviderTargetPolicy>();
        services.AddSingleton<IDevelopmentProviderGateway, DevelopmentProviderGateway>();
        services.AddSingleton<IDevelopmentCredentialProtector, DevelopmentCredentialProtectorAdapter>();
        services.AddScoped<IDevelopmentIntegrationAuthorization, DevelopmentIntegrationAuthorizationAdapter>();
        services.AddScoped<IDevelopmentProjectDirectory, DevelopmentProjectDirectoryAdapter>();
        services.AddScoped<DevelopmentIntegrationService>();
        services.AddScoped<CreateConnectionHandler>();
        services.AddScoped<ListConnectionsHandler>();
        services.AddScoped<GetConnectionHandler>();
        services.AddScoped<RotateCredentialHandler>();
        services.AddScoped<RotateWebhookSecretHandler>();
        services.AddScoped<DisconnectConnectionHandler>();
        services.AddScoped<DeleteConnectionHandler>();
        services.AddScoped<ListConnectionMappingsHandler>();
        services.AddScoped<CreateMappingHandler>();
        services.AddScoped<DeleteMappingHandler>();
        services.AddScoped<ListWorkItemMappingsHandler>();
        services.AddScoped<ListWorkItemLinksHandler>();
        services.AddScoped<CreateWorkItemLinkHandler>();
        services.AddScoped<DeleteWorkItemLinkHandler>();
        services.AddScoped<ReceiveWebhookHandler>();
        services.AddScoped<ApplyWebhookLinksHandler>();
        services.AddScoped<ProcessWebhookHandler>();
        services.AddScoped<CheckProviderHealthHandler>();
        services.AddScoped<ListRepositoriesHandler>();
        services.AddScoped<DevelopmentWebhookReceiptRetentionService>();
        services.AddScoped<IDurableEventHandler, WorkItemAuditDurableHandler>();
        services.AddScoped<IDurableEventHandler, WorkItemNotificationDurableHandler>();
        services.AddScoped<IDurableEventHandler, WorkItemSearchUpsertDurableHandler>();
        services.AddScoped<IDurableEventHandler, WorkItemSearchDeleteDurableHandler>();
        services.AddScoped<IDurableEventHandler, WorkItemRealtimeDurableHandler>();
        services.AddScoped<IDurableEventHandler, WorkItemCacheInvalidationDurableHandler>();
        services.AddScoped<IDurableEventHandler, WorkItemWebhookDurableHandler>();
        services.AddScoped<IDurableEventHandler, WorkItemRecurrenceDurableHandler>();
        services.AddScoped<IDurableEventHandler, WorkItemBulkJobDurableHandler>();
        services.AddScoped<IDurableEventHandler, DevelopmentWebhookProcessingDurableHandler>();
        services.AddScoped<WorkItemTransactionFilter>();
        services.AddScoped<IWorkItemActivityStore, WorkItemActivityStore>();
        services.AddScoped<WorkItemActivityQueryService>();
        services.AddScoped<WorkItemService>();
        services.AddScoped<IIntakeWorkItemCreator>(provider =>
            provider.GetRequiredService<WorkItemService>());
        services.AddScoped<IIntakeRoutePolicy, IntakeRoutePolicyAdapter>();
        services.AddOptions<IntakeOptions>()
            .BindConfiguration("Intake")
            .Validate(
                options => options.MaxFields is >= 1 and <= 100
                    && options.MaxValues is >= 1 and <= 100
                    && options.MaxAttachments is >= 0 and <= 20
                    && options.MaxAttachmentBytes is >= 1_024 and <= 25 * 1024 * 1024
                    && options.MaxTotalAttachmentBytes >= options.MaxAttachmentBytes
                    && options.MaxTotalAttachmentBytes <= 25 * 1024 * 1024
                    && options.MaxValueCharacters is >= 100 and <= 20_000
                    && options.MaxTotalValueCharacters >= options.MaxValueCharacters
                    && options.MaxTotalValueCharacters <= 100_000,
                "Intake limits are outside the supported bounds.")
            .ValidateOnStart();
        services.AddScoped<IntakeFormService>();
        services.AddScoped<IntakeSubmissionService>();
        services.AddScoped<CreateWorkItemHandler>(provider => new CreateWorkItemHandler(
            provider.GetRequiredService<IDocumentRepository<WorkItemDocument>>(),
            provider.GetRequiredService<IWorkItemNotificationPublisher>(),
            provider.GetRequiredService<IWorkItemAuditPublisher>(),
            provider.GetRequiredService<IClock>(),
            provider.GetRequiredService<ICurrentUser>(),
            provider.GetRequiredService<IProjectPermissionChecker>(),
            provider.GetRequiredService<IWorkItemTeamPolicy>(),
            provider.GetRequiredService<IBoardPlacementPolicy>(),
            provider.GetRequiredService<IDistributedLockProvider>(),
            provider.GetRequiredService<IOptions<DistributedLockOptions>>(),
            provider.GetRequiredService<IWorkItemSearchPublisher>(),
            provider.GetRequiredService<IWorkItemRealtimePublisher>(),
            provider.GetRequiredService<IWorkItemCacheInvalidationPublisher>(),
            provider.GetRequiredService<IWorkItemActivityStore>(),
            provider.GetRequiredService<WorkItemGraphService>(),
            provider.GetService<WorkItemWipProjection>(),
            provider.GetRequiredService<WorkItemRankService>(),
            provider.GetRequiredService<IWorkItemTypeSchemaPolicy>(),
            provider.GetService<WorkItemCollaborationService>(),
            provider.GetService<IWorkItemAutomationEventPublisher>(),
            provider.GetService<IWorkItemAutomationChainContextAccessor>()));
        services.AddScoped<SearchWorkItemsHandler>(provider => new SearchWorkItemsHandler(
            provider.GetRequiredService<IDocumentRepository<WorkItemDocument>>(),
            provider.GetRequiredService<ICurrentUser>(),
            provider.GetRequiredService<IProjectPermissionChecker>(),
            provider.GetRequiredService<IWorkItemTypeSchemaPolicy>(),
            provider.GetRequiredService<IWorkItemSearchIndex>(),
            provider.GetRequiredService<IWorkItemActivityStore>(),
            provider.GetRequiredService<IOptions<SearchOptions>>()));
        services.AddScoped<GetWorkItemHandler>(provider => new GetWorkItemHandler(
            provider.GetRequiredService<IDocumentRepository<WorkItemDocument>>(),
            provider.GetRequiredService<ICurrentUser>(),
            provider.GetRequiredService<IProjectPermissionChecker>(),
            provider.GetRequiredService<IWorkItemActivityStore>()));
        services.AddScoped<ArchiveWorkItemHandler>(provider => new ArchiveWorkItemHandler(
            provider.GetRequiredService<IDocumentRepository<WorkItemDocument>>(),
            provider.GetRequiredService<IWorkItemAuditPublisher>(),
            provider.GetRequiredService<IClock>(),
            provider.GetRequiredService<ICurrentUser>(),
            provider.GetRequiredService<IProjectPermissionChecker>(),
            provider.GetRequiredService<IDistributedLockProvider>(),
            provider.GetRequiredService<IOptions<DistributedLockOptions>>(),
            provider.GetRequiredService<IWorkItemSearchPublisher>(),
            provider.GetRequiredService<IWorkItemRealtimePublisher>(),
            provider.GetRequiredService<IWorkItemCacheInvalidationPublisher>(),
            provider.GetRequiredService<IWorkItemActivityStore>(),
            provider.GetService<IExpectedVersionAccessor>(),
            provider.GetService<WorkItemWipProjection>(),
            provider.GetService<WorkItemCollaborationService>()));
        services.AddScoped<RestoreWorkItemHandler>(provider => new RestoreWorkItemHandler(
            provider.GetRequiredService<IDocumentRepository<WorkItemDocument>>(),
            provider.GetRequiredService<IWorkItemAuditPublisher>(),
            provider.GetRequiredService<IClock>(),
            provider.GetRequiredService<ICurrentUser>(),
            provider.GetRequiredService<IProjectPermissionChecker>(),
            provider.GetRequiredService<IBoardPlacementPolicy>(),
            provider.GetRequiredService<IDistributedLockProvider>(),
            provider.GetRequiredService<IOptions<DistributedLockOptions>>(),
            provider.GetRequiredService<IWorkItemSearchPublisher>(),
            provider.GetRequiredService<IWorkItemRealtimePublisher>(),
            provider.GetRequiredService<IWorkItemCacheInvalidationPublisher>(),
            provider.GetRequiredService<IWorkItemActivityStore>(),
            provider.GetService<IExpectedVersionAccessor>(),
            provider.GetService<WorkItemWipProjection>(),
            provider.GetRequiredService<WorkItemRankService>(),
            provider.GetService<WorkItemCollaborationService>()));
        services.AddScoped<AddLabelHandler>(provider => new AddLabelHandler(
            provider.GetRequiredService<IDocumentRepository<WorkItemDocument>>(),
            provider.GetRequiredService<IClock>(),
            provider.GetRequiredService<ICurrentUser>(),
            provider.GetRequiredService<IProjectPermissionChecker>(),
            provider.GetRequiredService<IWorkItemSearchPublisher>(),
            provider.GetRequiredService<IWorkItemActivityStore>(),
            provider.GetService<IExpectedVersionAccessor>(),
            provider.GetService<WorkItemCollaborationService>(),
            provider.GetService<IWorkItemAutomationEventPublisher>(),
            provider.GetService<IWorkItemAutomationChainContextAccessor>()));
        services.AddScoped<RemoveLabelHandler>(provider => new RemoveLabelHandler(
            provider.GetRequiredService<IDocumentRepository<WorkItemDocument>>(),
            provider.GetRequiredService<IClock>(),
            provider.GetRequiredService<ICurrentUser>(),
            provider.GetRequiredService<IProjectPermissionChecker>(),
            provider.GetRequiredService<IWorkItemSearchPublisher>(),
            provider.GetRequiredService<IWorkItemActivityStore>(),
            provider.GetService<IExpectedVersionAccessor>(),
            provider.GetService<WorkItemCollaborationService>(),
            provider.GetService<IWorkItemAutomationEventPublisher>(),
            provider.GetService<IWorkItemAutomationChainContextAccessor>()));
        services.AddScoped<AddChecklistItemHandler>(provider => new AddChecklistItemHandler(
            provider.GetRequiredService<IDocumentRepository<WorkItemDocument>>(),
            provider.GetRequiredService<IClock>(),
            provider.GetRequiredService<ICurrentUser>(),
            provider.GetRequiredService<IProjectPermissionChecker>(),
            provider.GetRequiredService<IWorkItemActivityStore>(),
            provider.GetService<IExpectedVersionAccessor>(),
            provider.GetService<WorkItemCollaborationService>()));
        services.AddScoped<CompleteChecklistItemHandler>(provider => new CompleteChecklistItemHandler(
            provider.GetRequiredService<IDocumentRepository<WorkItemDocument>>(),
            provider.GetRequiredService<IClock>(),
            provider.GetRequiredService<ICurrentUser>(),
            provider.GetRequiredService<IProjectPermissionChecker>(),
            provider.GetRequiredService<IWorkItemActivityStore>(),
            provider.GetService<IExpectedVersionAccessor>(),
            provider.GetService<WorkItemCollaborationService>()));
        services.AddScoped<AddWorkLogHandler>(provider => new AddWorkLogHandler(
            provider.GetRequiredService<IDocumentRepository<WorkItemDocument>>(),
            provider.GetRequiredService<IClock>(),
            provider.GetRequiredService<ICurrentUser>(),
            provider.GetRequiredService<IProjectPermissionChecker>(),
            provider.GetRequiredService<IWorkItemActivityStore>(),
            provider.GetService<IExpectedVersionAccessor>(),
            provider.GetRequiredService<IWorkItemCacheInvalidationPublisher>(),
            provider.GetService<WorkItemCollaborationService>()));
        services.AddScoped<SetPlanningHandler>(provider => new SetPlanningHandler(
            provider.GetRequiredService<IDocumentRepository<WorkItemDocument>>(),
            provider.GetRequiredService<IClock>(),
            provider.GetRequiredService<ICurrentUser>(),
            provider.GetRequiredService<IProjectPermissionChecker>(),
            provider.GetService<IWorkItemSprintPolicy>(),
            provider.GetRequiredService<IWorkItemSearchPublisher>(),
            provider.GetRequiredService<IWorkItemActivityStore>(),
            provider.GetService<IExpectedVersionAccessor>(),
            provider.GetRequiredService<IWorkItemCacheInvalidationPublisher>(),
            provider.GetService<WorkItemCollaborationService>()));
        services.AddScoped<MoveWorkItemHandler>(provider => new MoveWorkItemHandler(
            provider.GetRequiredService<IDocumentRepository<WorkItemDocument>>(),
            provider.GetRequiredService<IWorkItemAuditPublisher>(),
            provider.GetRequiredService<IClock>(),
            provider.GetRequiredService<ICurrentUser>(),
            provider.GetRequiredService<IProjectPermissionChecker>(),
            provider.GetRequiredService<IWorkflowPolicy>(),
            provider.GetRequiredService<IBoardPlacementPolicy>(),
            provider.GetRequiredService<IDistributedLockProvider>(),
            provider.GetRequiredService<IOptions<DistributedLockOptions>>(),
            provider.GetRequiredService<IWorkItemSearchPublisher>(),
            provider.GetRequiredService<IWorkItemRealtimePublisher>(),
            provider.GetRequiredService<IWorkItemCacheInvalidationPublisher>(),
            provider.GetRequiredService<IWorkItemActivityStore>(),
            provider.GetRequiredService<WorkItemGraphService>(),
            provider.GetService<IExpectedVersionAccessor>(),
            provider.GetService<WorkItemWipProjection>(),
            provider.GetRequiredService<WorkItemRankService>(),
            provider.GetService<WorkItemCollaborationService>(),
            provider.GetService<IWorkItemAutomationEventPublisher>(),
            provider.GetService<IWorkItemAutomationChainContextAccessor>()));
        services.AddScoped<ClearAssigneeHandler>(provider => new ClearAssigneeHandler(
            provider.GetRequiredService<IDocumentRepository<WorkItemDocument>>(),
            provider.GetRequiredService<IWorkItemAuditPublisher>(),
            provider.GetRequiredService<IClock>(),
            provider.GetRequiredService<ICurrentUser>(),
            provider.GetRequiredService<IProjectPermissionChecker>(),
            provider.GetRequiredService<IWorkItemSearchPublisher>(),
            provider.GetRequiredService<IWorkItemRealtimePublisher>(),
            provider.GetRequiredService<IWorkItemCacheInvalidationPublisher>(),
            provider.GetRequiredService<IWorkItemActivityStore>(),
            provider.GetService<IExpectedVersionAccessor>(),
            provider.GetService<WorkItemCollaborationService>(),
            provider.GetService<IWorkItemAutomationEventPublisher>(),
            provider.GetService<IWorkItemAutomationChainContextAccessor>()));
        services.AddScoped<AssignWorkItemHandler>(provider => new AssignWorkItemHandler(
            provider.GetRequiredService<IDocumentRepository<WorkItemDocument>>(),
            provider.GetRequiredService<IWorkItemAuditPublisher>(),
            provider.GetRequiredService<IClock>(),
            provider.GetRequiredService<ICurrentUser>(),
            provider.GetRequiredService<IProjectPermissionChecker>(),
            provider.GetRequiredService<IWorkItemSearchPublisher>(),
            provider.GetRequiredService<IWorkItemRealtimePublisher>(),
            provider.GetRequiredService<IWorkItemCacheInvalidationPublisher>(),
            provider.GetRequiredService<IWorkItemActivityStore>(),
            provider.GetService<IExpectedVersionAccessor>(),
            provider.GetService<WorkItemCollaborationService>(),
            provider.GetRequiredService<IWorkItemTeamPolicy>(),
            provider.GetRequiredService<IWorkItemNotificationPublisher>()));
        services.AddScoped<SetWorkItemTeamHandler>(provider => new SetWorkItemTeamHandler(
            provider.GetRequiredService<IDocumentRepository<WorkItemDocument>>(),
            provider.GetRequiredService<IWorkItemAuditPublisher>(),
            provider.GetRequiredService<IClock>(),
            provider.GetRequiredService<ICurrentUser>(),
            provider.GetRequiredService<IProjectPermissionChecker>(),
            provider.GetRequiredService<IWorkItemSearchPublisher>(),
            provider.GetRequiredService<IWorkItemRealtimePublisher>(),
            provider.GetRequiredService<IWorkItemCacheInvalidationPublisher>(),
            provider.GetRequiredService<IWorkItemActivityStore>(),
            provider.GetService<IExpectedVersionAccessor>(),
            provider.GetService<WorkItemCollaborationService>(),
            provider.GetRequiredService<IWorkItemTeamPolicy>()));
        services.AddScoped<SetCustomFieldsHandler>(provider => new SetCustomFieldsHandler(
            provider.GetRequiredService<IDocumentRepository<WorkItemDocument>>(),
            provider.GetRequiredService<IWorkItemAuditPublisher>(),
            provider.GetRequiredService<IClock>(),
            provider.GetRequiredService<ICurrentUser>(),
            provider.GetRequiredService<IProjectPermissionChecker>(),
            provider.GetRequiredService<IWorkItemTypeSchemaPolicy>(),
            provider.GetRequiredService<IWorkItemSearchPublisher>(),
            provider.GetRequiredService<IWorkItemRealtimePublisher>(),
            provider.GetRequiredService<IWorkItemCacheInvalidationPublisher>(),
            provider.GetRequiredService<IWorkItemActivityStore>(),
            provider.GetService<IExpectedVersionAccessor>(),
            provider.GetService<WorkItemCollaborationService>(),
            provider.GetService<IWorkItemAutomationEventPublisher>(),
            provider.GetService<IWorkItemAutomationChainContextAccessor>()));
        services.AddScoped<RequestApprovalHandler>(provider => new RequestApprovalHandler(
            provider.GetRequiredService<IDocumentRepository<WorkItemDocument>>(),
            provider.GetRequiredService<IWorkItemNotificationPublisher>(),
            provider.GetRequiredService<IWorkItemAuditPublisher>(),
            provider.GetRequiredService<IClock>(),
            provider.GetRequiredService<ICurrentUser>(),
            provider.GetRequiredService<IProjectPermissionChecker>(),
            provider.GetRequiredService<IWorkflowPolicy>(),
            provider.GetRequiredService<IWorkItemActivityStore>(),
            provider.GetService<IExpectedVersionAccessor>(),
            provider.GetService<WorkItemCollaborationService>()));
        services.AddScoped<UpdateWorkItemHandler>(provider => new UpdateWorkItemHandler(
            provider.GetRequiredService<IDocumentRepository<WorkItemDocument>>(),
            provider.GetRequiredService<IWorkItemAuditPublisher>(),
            provider.GetRequiredService<IClock>(),
            provider.GetRequiredService<ICurrentUser>(),
            provider.GetRequiredService<IProjectPermissionChecker>(),
            provider.GetRequiredService<IWorkItemSearchPublisher>(),
            provider.GetRequiredService<IWorkItemRealtimePublisher>(),
            provider.GetRequiredService<IWorkItemCacheInvalidationPublisher>(),
            provider.GetRequiredService<IWorkItemActivityStore>(),
            provider.GetService<IExpectedVersionAccessor>(),
            provider.GetService<WorkItemCollaborationService>(),
            provider.GetService<IWorkItemAutomationEventPublisher>(),
            provider.GetService<IWorkItemAutomationChainContextAccessor>()));
        services.AddScoped<DecideApprovalHandler>(provider => new DecideApprovalHandler(
            provider.GetRequiredService<IDocumentRepository<WorkItemDocument>>(),
            provider.GetRequiredService<IWorkItemNotificationPublisher>(),
            provider.GetRequiredService<IWorkItemAuditPublisher>(),
            provider.GetRequiredService<IClock>(),
            provider.GetRequiredService<ICurrentUser>(),
            provider.GetRequiredService<IProjectPermissionChecker>(),
            provider.GetRequiredService<IWorkItemActivityStore>(),
            provider.GetService<IExpectedVersionAccessor>(),
            provider.GetService<WorkItemCollaborationService>()));
        if (configuration?.GetValue("BackgroundJobs:Enabled", true) == true)
        {
            services.AddHostedService<DueDateReminderHostedService>();
            services.AddHostedService<WorkItemRecurrenceSchedulerHostedService>();
            services.AddHostedService<WebhookDispatcherHostedService>();
            services.AddHostedService<DevelopmentWebhookReceiptRetentionHostedService>();
        }
        return services;
    }
}
