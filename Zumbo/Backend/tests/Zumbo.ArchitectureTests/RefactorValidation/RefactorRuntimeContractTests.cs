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
        "services.AddScoped<SearchWorkItemsHandler>();",
        "services.AddScoped<ListNotificationsHandler>();",
        "services.AddScoped<MarkNotificationAsReadHandler>();",
        "services.AddScoped<WriteAuditLogHandler>();",
        "services.AddScoped<QueryAuditLogHandler>();",
        "services.AddScoped<IWorkItemWebhookDelivery,WorkItemWebhookDeliveryAdapter>();",
        "services.AddScoped<IDurableEventHandler,DevelopmentWebhookDurableHandler>();"
    ];

    private static readonly string[] PortFocusedVerticalSliceDiRegistrations =
    [
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
        "services.AddScoped<SearchWorkItemsHandler>(provider=>newSearchWorkItemsHandler("
        + "provider.GetRequiredService<IDocumentRepository<WorkItemDocument>>(),"
        + "provider.GetRequiredService<ICurrentUser>(),"
        + "provider.GetRequiredService<IProjectPermissionChecker>(),"
        + "provider.GetRequiredService<IWorkItemTypeSchemaPolicy>(),"
        + "provider.GetRequiredService<IWorkItemSearchIndex>(),"
        + "provider.GetRequiredService<IWorkItemActivityStore>(),"
        + "provider.GetRequiredService<IOptions<SearchOptions>>()));",
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
        "ingress.MapPost(\"/{connectionId}/webhook\",ReceiveWebhookAsync).AllowAnonymous();"
    ];

    private static readonly string[] PortFocusedVerticalSliceEndpointMappings =
    [
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
                "RegisterUserHandler and SearchUsersHandler remain scoped self-services; explicit factories select their port-focused constructors while compatibility constructors remain available.",
                "CreateOrganizationHandler and ListOrganizationsHandler remain scoped self-services; explicit factories select their port-focused constructors while compatibility constructors remain available.",
                "CreateTeamHandler and ListTeamsHandler remain scoped self-services; explicit factories select their port-focused constructors while compatibility constructors remain available.",
                "CreateProjectHandler and ListProjectsHandler remain scoped self-services; explicit factories select their port-focused constructors while compatibility constructors remain available.",
                "CreateBoardHandler and ListBoardsByProjectHandler remain scoped self-services; explicit factories select their port-focused constructors while compatibility constructors remain available.",
                "UpsertWorkflowHandler and GetWorkflowHandler remain scoped self-services; explicit factories select their port-focused constructors while compatibility constructors remain available.",
                "CreateWorkItemHandler and SearchWorkItemsHandler remain scoped self-services; explicit factories select their port-focused constructors while compatibility constructors remain available.",
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
        name.Contains("Event", StringComparison.Ordinal)
        || name.Contains("Message", StringComparison.Ordinal)
        || name.Contains("Inbox", StringComparison.Ordinal)
        || name.Contains("Outbox", StringComparison.Ordinal)
        || name.Contains("DeadLetter", StringComparison.Ordinal);

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
