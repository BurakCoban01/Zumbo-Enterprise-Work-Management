using Zumbo.Modules.WorkItems;
using Zumbo.BuildingBlocks.Application.Messaging;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

using static ApiEndpointResults;

internal static class WorkItemEndpoints
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
        services.AddScoped<WorkItemWebhookService>();
        services.AddScoped<IWorkItemWebhookDelivery, WorkItemWebhookDeliveryAdapter>();
        services.AddScoped<IDurableEventHandler, WorkItemAuditDurableHandler>();
        services.AddScoped<IDurableEventHandler, WorkItemNotificationDurableHandler>();
        services.AddScoped<IDurableEventHandler, WorkItemSearchUpsertDurableHandler>();
        services.AddScoped<IDurableEventHandler, WorkItemSearchDeleteDurableHandler>();
        services.AddScoped<IDurableEventHandler, WorkItemRealtimeDurableHandler>();
        services.AddScoped<IDurableEventHandler, WorkItemCacheInvalidationDurableHandler>();
        services.AddScoped<IDurableEventHandler, WorkItemWebhookDurableHandler>();
        services.AddScoped<IDurableEventHandler, WorkItemRecurrenceDurableHandler>();
        services.AddScoped<IDurableEventHandler, WorkItemBulkJobDurableHandler>();
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
        services.AddScoped<CreateWorkItemHandler>();
        services.AddScoped<SearchWorkItemsHandler>();
        if (configuration?.GetValue("BackgroundJobs:Enabled", true) == true)
        {
            services.AddHostedService<DueDateReminderHostedService>();
            services.AddHostedService<WorkItemRecurrenceSchedulerHostedService>();
            services.AddHostedService<WebhookDispatcherHostedService>();
        }
        return services;
    }

    internal static void MapWorkItemEndpoints(this RouteGroupBuilder api)
    {
        var group = api.MapGroup("/work-items")
            .WithTags("WorkItems")
            .RequireAuthorization()
            .WithZumboPermission(PermissionCatalog.WorkItemView);
        group.AddEndpointFilter<WorkItemTransactionFilter>();

        group.MapGet("/durable-messaging/metrics", async (
            IDurableEventOutbox outbox,
            IClock clock,
            CancellationToken ct) =>
            Results.Ok(await outbox.GetMetricsAsync(clock.UtcNow, ct)))
            .WithZumboPermission(PermissionCatalog.OperationsManage, isGlobal: true);

        group.MapGet("/durable-messaging/dead-letters", async (
            int? pageSize,
            IDurableEventOutbox outbox,
            CancellationToken ct) =>
            Results.Ok(await outbox.ListDeadLettersAsync(
                Math.Clamp(pageSize ?? 20, 1, 50),
                ct)))
            .WithZumboPermission(PermissionCatalog.OperationsManage, isGlobal: true)
            .RequireRateLimiting("report");

        group.MapPost("/durable-messaging/dead-letter/{messageId}/replay", async (
            string messageId,
            IDurableEventOutbox outbox,
            IWorkItemOperationsAuditWriter audit,
            IClock clock,
            HttpContext http,
            CancellationToken ct) =>
        {
            var replayed = await outbox.ReplayDeadLetterAsync(messageId, clock.UtcNow, ct);
            if (replayed)
            {
                await audit.WriteAsync(
                    "DurableMessageReplayed",
                    "DurableMessage",
                    messageId,
                    "DeadLetter",
                    "Pending",
                    CorrelationId(http),
                    ct);
            }

            return Results.Ok(new { replayed });
        })
            .WithZumboPermission(PermissionCatalog.OperationsManage, isGlobal: true);

        group.MapPost("/search/rebuild", async (
            SearchMaintenanceService service,
            IWorkItemOperationsAuditWriter audit,
            HttpContext http,
            CancellationToken ct) =>
        {
            var result = await service.RebuildAsync(ct);
            await audit.WriteAsync(
                "SearchIndexRebuilt",
                "Operations",
                "work-item-search",
                null,
                $"{result.Indexed}:{result.Removed}:{result.AliasChanged}",
                CorrelationId(http),
                ct);
            return Results.Ok(result);
        })
            .WithZumboPermission(PermissionCatalog.OperationsManage, isGlobal: true)
            .RequireRateLimiting("bulk");

        group.MapPost("/search/reconcile", async (
            SearchMaintenanceService service,
            IWorkItemOperationsAuditWriter audit,
            HttpContext http,
            CancellationToken ct) =>
        {
            var result = await service.RebuildAsync(ct);
            await audit.WriteAsync(
                "SearchIndexReconciled",
                "Operations",
                "work-item-search",
                null,
                $"{result.Indexed}:{result.Removed}:{result.AliasChanged}",
                CorrelationId(http),
                ct);
            return Results.Ok(result);
        })
            .WithZumboPermission(PermissionCatalog.OperationsManage, isGlobal: true)
            .RequireRateLimiting("bulk");

        group.MapPost("/search", async (
            WorkItemSearchRequest request,
            WorkItemService service,
            HttpContext http,
            CancellationToken ct) =>
            Ok(await service.SearchPageAsync(request, ct), http))
            .RequireRateLimiting("search");

        group.MapGet("/", async (
            string? projectId,
            string? assigneeUserId,
            string? status,
            string? text,
            int? page,
            int? pageSize,
            bool? archived,
            string? issueType,
            string? customFieldKey,
            string? customFieldValue,
            SearchWorkItemsHandler handler,
            HttpContext http,
            CancellationToken ct) =>
            Ok(await handler.HandleAsync(
                new WorkItemSearchRequest(
                    projectId,
                    assigneeUserId,
                    status,
                    text,
                    page ?? 1,
                    pageSize ?? 100,
                    archived ?? false,
                    issueType,
                    customFieldKey,
                    customFieldValue),
                ct), http))
            .RequireRateLimiting("search");

        group.MapGet("/{id}", async (string id, WorkItemService service, HttpContext http, CancellationToken ct) =>
            Ok(await service.GetAsync(id, ct), http));

        group.MapGet("/{id}/collaboration", async (
            string id,
            WorkItemCollaborationService service,
            HttpContext http,
            CancellationToken ct) =>
            Ok(await service.GetAsync(id, ct), http));

        group.MapPut("/{id}/watch", async (
            string id,
            SetWorkItemWatchRequest request,
            WorkItemCollaborationService service,
            HttpContext http,
            CancellationToken ct) =>
            Ok(await service.SetWatchingAsync(id, request.Watching, CorrelationId(http), ct), http));

        group.MapPut("/{id}/vote", async (
            string id,
            SetWorkItemVoteRequest request,
            WorkItemCollaborationService service,
            HttpContext http,
            CancellationToken ct) =>
            Ok(await service.SetVoteAsync(id, request.Voted, CorrelationId(http), ct), http));

        group.MapGet("/{id}/activity", async (
            string id,
            int? page,
            int? pageSize,
            WorkItemCollaborationService service,
            HttpContext http,
            CancellationToken ct) =>
            Ok(await service.ListActivityAsync(id, page ?? 1, pageSize ?? 50, ct), http));

        group.MapGet("/templates", async (
            string projectId,
            int? page,
            int? pageSize,
            bool? includeArchived,
            WorkItemTemplateRecurrenceService service,
            HttpContext http,
            CancellationToken ct) =>
            Ok(await service.ListTemplatesAsync(
                projectId, page ?? 1, pageSize ?? 50, includeArchived ?? false, ct), http));

        group.MapPost("/templates", async (
            CreateWorkItemTemplateRequest request,
            WorkItemTemplateRecurrenceService service,
            HttpContext http,
            CancellationToken ct) =>
            Created(await service.CreateTemplateAsync(request, CorrelationId(http), ct), http))
            .WithZumboPermission(PermissionCatalog.WorkItemCreate);

        group.MapPut("/templates/{templateId}", async (
            string templateId,
            UpdateWorkItemTemplateRequest request,
            WorkItemTemplateRecurrenceService service,
            HttpContext http,
            CancellationToken ct) =>
            Ok(await service.UpdateTemplateAsync(templateId, request, CorrelationId(http), ct), http))
            .WithZumboPermission(PermissionCatalog.WorkItemUpdate);

        group.MapDelete("/templates/{templateId}", async (
            string templateId,
            WorkItemTemplateRecurrenceService service,
            HttpContext http,
            CancellationToken ct) =>
        {
            await service.ArchiveTemplateAsync(templateId, CorrelationId(http), ct);
            return Results.NoContent();
        }).WithZumboPermission(PermissionCatalog.WorkItemUpdate);

        group.MapGet("/recurrences", async (
            string projectId,
            int? page,
            int? pageSize,
            bool? includeArchived,
            WorkItemTemplateRecurrenceService service,
            HttpContext http,
            CancellationToken ct) =>
            Ok(await service.ListRecurrencesAsync(
                projectId, page ?? 1, pageSize ?? 50, includeArchived ?? false, ct), http));

        group.MapPost("/recurrences", async (
            CreateWorkItemRecurrenceRequest request,
            WorkItemTemplateRecurrenceService service,
            HttpContext http,
            CancellationToken ct) =>
            Created(await service.CreateRecurrenceAsync(request, CorrelationId(http), ct), http))
            .WithZumboPermission(PermissionCatalog.WorkItemCreate);

        group.MapPost("/recurrences/preview", async (
            PreviewWorkItemRecurrenceRequest request,
            WorkItemTemplateRecurrenceService service,
            HttpContext http,
            CancellationToken ct) =>
            Ok(await service.PreviewRecurrenceAsync(request, ct), http))
            .WithZumboPermission(PermissionCatalog.WorkItemCreate);

        group.MapPatch("/recurrences/{recurrenceId}/state", async (
            string recurrenceId,
            SetWorkItemRecurrenceStateRequest request,
            WorkItemTemplateRecurrenceService service,
            HttpContext http,
            CancellationToken ct) =>
            Ok(await service.SetRecurrenceStateAsync(
                recurrenceId, request.Active, CorrelationId(http), ct), http))
            .WithZumboPermission(PermissionCatalog.WorkItemUpdate);

        group.MapDelete("/recurrences/{recurrenceId}", async (
            string recurrenceId,
            WorkItemTemplateRecurrenceService service,
            HttpContext http,
            CancellationToken ct) =>
        {
            await service.ArchiveRecurrenceAsync(recurrenceId, CorrelationId(http), ct);
            return Results.NoContent();
        }).WithZumboPermission(PermissionCatalog.WorkItemUpdate);

        group.MapGet("/recurrences/{recurrenceId}/occurrences", async (
            string recurrenceId,
            int? page,
            int? pageSize,
            WorkItemTemplateRecurrenceService service,
            HttpContext http,
            CancellationToken ct) =>
            Ok(await service.ListOccurrencesAsync(recurrenceId, page ?? 1, pageSize ?? 50, ct), http));

        group.MapPost("/recurrences/process-due", async (
            WorkItemTemplateRecurrenceService service,
            CancellationToken ct) =>
            Results.Ok(new { scheduled = await service.ScheduleDueAsync(ct) }))
            .WithZumboPermission(PermissionCatalog.OperationsManage, isGlobal: true);

        group.MapPost("/", async (CreateWorkItemRequest request, CreateWorkItemHandler handler, HttpContext http, CancellationToken ct) =>
            Created(await handler.HandleAsync(request, CorrelationId(http), ct), http))
            .WithZumboPermission(PermissionCatalog.WorkItemCreate);

        group.MapPost("/bulk/jobs/import", async (
            CreateWorkItemImportJobRequest request,
            WorkItemBulkJobService service,
            HttpContext http,
            CancellationToken ct) =>
            Created(await service.SubmitImportAsync(request, IdempotencyKey(http), CorrelationId(http), ct), http))
            .WithZumboPermission(PermissionCatalog.WorkItemCreate)
            .RequireRateLimiting("bulk");

        group.MapPost("/bulk/jobs/export", async (
            CreateWorkItemExportJobRequest request,
            WorkItemBulkJobService service,
            HttpContext http,
            CancellationToken ct) =>
            Created(await service.SubmitExportAsync(request, IdempotencyKey(http), CorrelationId(http), ct), http))
            .WithZumboPermission(PermissionCatalog.WorkItemView)
            .RequireRateLimiting("bulk");

        group.MapPost("/bulk/jobs", async (
            CreateWorkItemBulkJobRequest request,
            WorkItemBulkJobService service,
            HttpContext http,
            CancellationToken ct) =>
            Created(await service.SubmitBulkAsync(request, IdempotencyKey(http), CorrelationId(http), ct), http))
            .WithZumboPermission(PermissionCatalog.WorkItemUpdate)
            .RequireRateLimiting("bulk");

        group.MapGet("/bulk/jobs", async (
            string projectId, int? page, int? pageSize,
            WorkItemBulkJobService service, HttpContext http, CancellationToken ct) =>
            Ok(await service.ListAsync(projectId, page ?? 1, pageSize ?? 50, ct), http));

        group.MapGet("/bulk/jobs/{jobId}", async (
            string jobId, WorkItemBulkJobService service, HttpContext http, CancellationToken ct) =>
            Ok(await service.GetAsync(jobId, ct), http));

        group.MapPost("/bulk/jobs/{jobId}/cancel", async (
            string jobId, WorkItemBulkJobService service, HttpContext http, CancellationToken ct) =>
            Ok(await service.CancelAsync(jobId, CorrelationId(http), ct), http))
            .WithZumboPermission(PermissionCatalog.WorkItemUpdate);

        group.MapPost("/bulk/jobs/{jobId}/retry", async (
            string jobId, WorkItemBulkJobService service, HttpContext http, CancellationToken ct) =>
            Ok(await service.RetryAsync(jobId, CorrelationId(http), ct), http))
            .WithZumboPermission(PermissionCatalog.WorkItemUpdate);

        group.MapGet("/bulk/jobs/{jobId}/result", async (
            string jobId, WorkItemBulkJobService service, CancellationToken ct) =>
        {
            var file = await service.OpenArtifactAsync(jobId, errors: false, ct);
            return Results.File(file.Content, file.ContentType, file.FileName, enableRangeProcessing: false);
        });

        group.MapGet("/bulk/jobs/{jobId}/errors", async (
            string jobId, WorkItemBulkJobService service, CancellationToken ct) =>
        {
            var file = await service.OpenArtifactAsync(jobId, errors: true, ct);
            return Results.File(file.Content, file.ContentType, file.FileName, enableRangeProcessing: false);
        });

        group.MapPost("/bulk/move", async (BulkMoveWorkItemsRequest request, WorkItemService service, HttpContext http, CancellationToken ct) =>
            Ok(await service.BulkMoveAsync(request, CorrelationId(http), ct), http))
            .WithZumboPermission(PermissionCatalog.WorkItemMove)
            .RequireRateLimiting("bulk");

        group.MapPost("/bulk/assign", async (BulkAssignWorkItemsRequest request, WorkItemService service, HttpContext http, CancellationToken ct) =>
            Ok(await service.BulkAssignAsync(request, CorrelationId(http), ct), http))
            .WithZumboPermission(PermissionCatalog.WorkItemAssign)
            .RequireRateLimiting("bulk");

        group.MapPost("/bulk/archive", async (BulkArchiveWorkItemsRequest request, WorkItemService service, HttpContext http, CancellationToken ct) =>
            Ok(await service.BulkArchiveAsync(request, CorrelationId(http), ct), http))
            .WithZumboPermission(PermissionCatalog.WorkItemDelete)
            .RequireRateLimiting("bulk");

        group.MapPut("/{id}", async (string id, UpdateWorkItemRequest request, WorkItemService service, HttpContext http, CancellationToken ct) =>
            Ok(await service.UpdateAsync(id, request, CorrelationId(http), ct), http))
            .WithZumboPermission(PermissionCatalog.WorkItemUpdate);

        group.MapPatch("/{id}/assignee", async (string id, AssignWorkItemRequest request, WorkItemService service, HttpContext http, CancellationToken ct) =>
            Ok(await service.AssignAsync(id, request, CorrelationId(http), ct), http))
            .WithZumboPermission(PermissionCatalog.WorkItemAssign);

        group.MapPatch("/{id}/status", async (string id, MoveWorkItemRequest request, WorkItemService service, HttpContext http, CancellationToken ct) =>
            Ok(await service.MoveAsync(id, request, CorrelationId(http), ct), http))
            .WithZumboPermission(PermissionCatalog.WorkItemMove);

        group.MapPatch("/{id}/rank", async (string id, ReorderWorkItemRequest request, WorkItemService service, HttpContext http, CancellationToken ct) =>
            Ok(await service.ReorderAsync(id, request, CorrelationId(http), ct), http))
            .WithZumboPermission(PermissionCatalog.WorkItemMove);

        group.MapPatch("/{id}/planning", async (string id, SetWorkItemPlanningRequest request, WorkItemService service, HttpContext http, CancellationToken ct) =>
            Ok(await service.SetPlanningAsync(id, request, ct), http))
            .WithZumboPermission(PermissionCatalog.WorkItemUpdate);

        group.MapPut("/{id}/custom-fields", async (
                string id,
                SetWorkItemCustomFieldsRequest request,
                WorkItemService service,
                HttpContext http,
                CancellationToken ct) =>
            Ok(await service.SetCustomFieldsAsync(id, request, CorrelationId(http), ct), http))
            .WithZumboPermission(PermissionCatalog.WorkItemUpdate);

        group.MapPatch("/{id}/parent", async (string id, SetWorkItemParentRequest request, WorkItemService service, HttpContext http, CancellationToken ct) =>
            Ok(await service.SetParentAsync(id, request, CorrelationId(http), ct), http))
            .WithZumboPermission(PermissionCatalog.WorkItemUpdate);

        group.MapPatch("/{id}/team", async (string id, SetWorkItemTeamRequest request, WorkItemService service, HttpContext http, CancellationToken ct) =>
            Ok(await service.SetTeamAsync(id, request, CorrelationId(http), ct), http))
            .WithZumboPermission(PermissionCatalog.WorkItemUpdate);

        group.MapPost("/{id}/approvals", async (string id, RequestWorkItemApprovalRequest request, WorkItemService service, HttpContext http, CancellationToken ct) =>
            Ok(await service.RequestApprovalAsync(id, request, CorrelationId(http), ct), http))
            .WithZumboPermission(PermissionCatalog.WorkItemApprove);

        group.MapPost("/{id}/approvals/{approvalId}/decision", async (
            string id,
            string approvalId,
            DecideWorkItemApprovalRequest request,
            WorkItemService service,
            HttpContext http,
            CancellationToken ct) =>
            Ok(await service.DecideApprovalAsync(id, approvalId, request, CorrelationId(http), ct), http))
            .WithZumboPermission(PermissionCatalog.WorkItemApprove);

        group.MapPost("/{id}/checklist", async (string id, AddChecklistItemRequest request, WorkItemService service, HttpContext http, CancellationToken ct) =>
            Ok(await service.AddChecklistItemAsync(id, request, ct), http))
            .WithZumboPermission(PermissionCatalog.WorkItemUpdate);

        group.MapPatch("/{id}/checklist/{itemId}", async (string id, string itemId, CompleteChecklistItemRequest request, WorkItemService service, HttpContext http, CancellationToken ct) =>
            Ok(await service.CompleteChecklistItemAsync(id, itemId, request, ct), http))
            .WithZumboPermission(PermissionCatalog.WorkItemUpdate);

        group.MapPost("/{id}/labels", async (string id, AddLabelRequest request, WorkItemService service, HttpContext http, CancellationToken ct) =>
            Ok(await service.AddLabelAsync(id, request, ct), http))
            .WithZumboPermission(PermissionCatalog.WorkItemUpdate);

        group.MapDelete("/{id}/labels/{label}", async (string id, string label, WorkItemService service, HttpContext http, CancellationToken ct) =>
            Ok(await service.RemoveLabelAsync(id, label, ct), http))
            .WithZumboPermission(PermissionCatalog.WorkItemUpdate);

        group.MapPost("/{id}/comments", async (string id, AddCommentRequest request, WorkItemService service, HttpContext http, CancellationToken ct) =>
            Ok(await service.AddCommentAsync(id, request, CorrelationId(http), ct), http))
            .WithZumboPermission(PermissionCatalog.CommentCreate);

        group.MapGet("/{id}/comments", async (string id, int? page, int? pageSize, WorkItemActivityQueryService service, HttpContext http, CancellationToken ct) =>
            Ok(await service.ListCommentsAsync(id, page ?? 1, pageSize ?? 50, ct), http));

        group.MapGet("/{id}/comments/{commentId}/revisions", async (string id, string commentId, int? page, int? pageSize, WorkItemActivityQueryService service, HttpContext http, CancellationToken ct) =>
            Ok(await service.ListRevisionsAsync(id, commentId, page ?? 1, pageSize ?? 50, ct), http));

        group.MapPut("/{id}/comments/{commentId}", async (string id, string commentId, EditCommentRequest request, WorkItemService service, HttpContext http, CancellationToken ct) =>
            Ok(await service.EditCommentAsync(id, commentId, request, CorrelationId(http), ct), http))
            .WithZumboPermission(PermissionCatalog.CommentCreate);

        group.MapDelete("/{id}/comments/{commentId}", async (string id, string commentId, WorkItemService service, HttpContext http, CancellationToken ct) =>
            Ok(await service.DeleteCommentAsync(id, commentId, CorrelationId(http), ct), http))
            .WithZumboPermission(PermissionCatalog.CommentCreate);

        group.MapPost("/{id}/attachments/upload", async (string id, IFormFile file, WorkItemService service, HttpContext http, CancellationToken ct) =>
        {
            await using var stream = file.OpenReadStream();
            return Ok(await service.UploadAttachmentAsync(
                id,
                stream,
                file.FileName,
                file.ContentType,
                file.Length,
                CorrelationId(http),
                ct), http);
        })
        .WithZumboPermission(PermissionCatalog.AttachmentCreate)
        .DisableAntiforgery()
        .RequireRateLimiting("upload");

        group.MapGet("/{id}/attachments/{attachmentId}/download", async (
            string id,
            string attachmentId,
            WorkItemService service,
            HttpContext http,
            CancellationToken ct) =>
        {
            var attachment = await service.OpenAttachmentAsync(id, attachmentId, ct);
            http.Response.Headers.CacheControl = "private, no-store";
            http.Response.Headers.Pragma = "no-cache";
            return Results.File(
                attachment.Content,
                attachment.ContentType,
                attachment.FileName,
                enableRangeProcessing: true);
        });

        group.MapGet("/{id}/attachments/{attachmentId}/preview", async (
            string id,
            string attachmentId,
            WorkItemService service,
            HttpContext http,
            CancellationToken ct) =>
        {
            var attachment = await service.OpenAttachmentAsync(id, attachmentId, ct);
            if (!IsPreviewableContentType(attachment.ContentType))
            {
                await attachment.Content.DisposeAsync();
                return Results.StatusCode(StatusCodes.Status415UnsupportedMediaType);
            }

            http.Response.Headers.CacheControl = "private, no-store";
            http.Response.Headers.Pragma = "no-cache";
            http.Response.Headers.ContentSecurityPolicy = "sandbox; default-src 'none'";
            http.Response.Headers["Cross-Origin-Resource-Policy"] = "same-origin";
            http.Response.Headers.ContentDisposition =
                $"inline; filename*=UTF-8''{Uri.EscapeDataString(attachment.FileName)}";
            return Results.Stream(
                attachment.Content,
                attachment.ContentType,
                enableRangeProcessing: true);
        });

        group.MapDelete("/{id}/attachments/{attachmentId}", async (string id, string attachmentId, WorkItemService service, HttpContext http, CancellationToken ct) =>
            Ok(await service.DeleteAttachmentAsync(id, attachmentId, CorrelationId(http), ct), http))
            .WithZumboPermission(PermissionCatalog.AttachmentDelete);

        group.MapGet("/{id}/attachments", async (string id, int? page, int? pageSize, WorkItemActivityQueryService service, HttpContext http, CancellationToken ct) =>
            Ok(await service.ListAttachmentsAsync(id, page ?? 1, pageSize ?? 50, ct), http));

        group.MapPost("/{id}/worklogs", async (string id, AddWorkLogRequest request, WorkItemService service, HttpContext http, CancellationToken ct) =>
            Ok(await service.AddWorkLogAsync(id, request, ct), http))
            .WithZumboPermission(PermissionCatalog.WorkLogCreate);

        group.MapGet("/{id}/worklogs", async (string id, int? page, int? pageSize, WorkItemActivityQueryService service, HttpContext http, CancellationToken ct) =>
            Ok(await service.ListWorkLogsAsync(id, page ?? 1, pageSize ?? 50, ct), http));

        group.MapGet("/{id}/approvals", async (string id, int? page, int? pageSize, WorkItemActivityQueryService service, HttpContext http, CancellationToken ct) =>
            Ok(await service.ListApprovalsAsync(id, page ?? 1, pageSize ?? 50, ct), http));

        group.MapGet("/{id}/timeline", async (string id, int? page, int? pageSize, WorkItemActivityQueryService service, HttpContext http, CancellationToken ct) =>
            Ok(await service.ListTimelineAsync(id, page ?? 1, pageSize ?? 50, ct), http));

        group.MapPost("/{id}/relations", async (string id, LinkWorkItemRequest request, WorkItemService service, HttpContext http, CancellationToken ct) =>
            Ok(await service.LinkAsync(id, request, CorrelationId(http), ct), http))
            .WithZumboPermission(PermissionCatalog.WorkItemLink);

        group.MapDelete("/{id}/relations/{relatedWorkItemId}", async (
            string id,
            string relatedWorkItemId,
            string relationType,
            WorkItemService service,
            HttpContext http,
            CancellationToken ct) =>
            Ok(await service.UnlinkAsync(id, relatedWorkItemId, relationType, CorrelationId(http), ct), http))
            .WithZumboPermission(PermissionCatalog.WorkItemLink);

        group.MapDelete("/{id}", async (string id, WorkItemService service, HttpContext http, CancellationToken ct) =>
        {
            await service.ArchiveAsync(id, CorrelationId(http), ct);
            return Ok(new { archived = true }, http);
        }).WithZumboPermission(PermissionCatalog.WorkItemDelete);

        group.MapPost("/{id}/restore", async (string id, WorkItemService service, HttpContext http, CancellationToken ct) =>
            Ok(await service.RestoreAsync(id, CorrelationId(http), ct), http))
            .WithZumboPermission(PermissionCatalog.WorkItemDelete);

        group.MapGet("/reports/project-summary/{projectId}", async (string projectId, WorkItemService service, HttpContext http, CancellationToken ct) =>
            ReportOk(await service.ProjectSummarySnapshotAsync(projectId, ct), http))
            .RequireRateLimiting("report");

        group.MapGet("/reports/status-distribution/{projectId}", async (string projectId, WorkItemService service, HttpContext http, CancellationToken ct) =>
            ReportOk(await service.StatusDistributionSnapshotAsync(projectId, ct), http))
            .RequireRateLimiting("report");

        group.MapGet("/reports/user-workload/{projectId}", async (string projectId, WorkItemService service, HttpContext http, CancellationToken ct) =>
            ReportOk(await service.UserWorkloadSnapshotAsync(projectId, ct), http))
            .RequireRateLimiting("report");

        group.MapGet("/reports/due-date-risks/{projectId}", async (string projectId, int? days, WorkItemService service, HttpContext http, CancellationToken ct) =>
            ReportOk(await service.DueDateRisksSnapshotAsync(projectId, days ?? 14, ct), http))
            .RequireRateLimiting("report");

        group.MapGet("/reports/sprint-burndown/{projectId}/{sprintId}", async (
            string projectId,
            string sprintId,
            DateOnly startDate,
            DateOnly endDate,
            SprintService service,
            HttpContext http,
            CancellationToken ct) =>
            ReportOk(await service.BurndownSnapshotAsync(projectId, sprintId, startDate, endDate, ct), http))
            .RequireRateLimiting("report");

        group.MapGet("/reports/sprint-velocity/{projectId}", async (
            string projectId,
            int? sprintCount,
            SprintService service,
            HttpContext http,
            CancellationToken ct) =>
            ReportOk(await service.VelocitySnapshotAsync(projectId, sprintCount ?? 6, ct), http))
            .RequireRateLimiting("report");

        group.MapGet("/reports/flow-time/{projectId}", async (
            string projectId,
            DateOnly? from,
            DateOnly? to,
            WorkItemService service,
            HttpContext http,
            CancellationToken ct) =>
            ReportOk(await service.FlowTimeSnapshotAsync(projectId, from, to, ct), http))
            .RequireRateLimiting("report");

        group.MapGet("/reports/completion-rate/{projectId}", async (
            string projectId,
            DateOnly? from,
            DateOnly? to,
            WorkItemService service,
            HttpContext http,
            CancellationToken ct) =>
            ReportOk(await service.CompletionRateSnapshotAsync(projectId, from, to, ct), http))
            .RequireRateLimiting("report");

        group.MapGet("/reports/team-performance/{projectId}", async (
            string projectId,
            DateOnly? from,
            DateOnly? to,
            WorkItemService service,
            HttpContext http,
            CancellationToken ct) =>
            ReportOk(await service.TeamPerformanceSnapshotAsync(projectId, from, to, ct), http))
            .RequireRateLimiting("report");
    }

    private static string IdempotencyKey(HttpContext http) =>
        http.Request.Headers["Idempotency-Key"].FirstOrDefault() ?? string.Empty;

    private static IResult ReportOk<T>(WorkItemReportSnapshot<T> snapshot, HttpContext http)
    {
        http.Response.Headers["X-Zumbo-Report-Generated-At"] = snapshot.GeneratedAt.ToString("O");
        http.Response.Headers["X-Zumbo-Report-Source-Version"] = snapshot.SourceVersion.ToString(
            System.Globalization.CultureInfo.InvariantCulture);
        http.Response.Headers["X-Zumbo-Report-Stale"] = snapshot.Stale ? "true" : "false";
        http.Response.Headers["X-Zumbo-Report-Age-Seconds"] = Math.Max(
            0,
            (DateTimeOffset.UtcNow - snapshot.GeneratedAt).TotalSeconds).ToString(
                "0.###",
                System.Globalization.CultureInfo.InvariantCulture);
        return Ok(snapshot.Data, http);
    }
}
