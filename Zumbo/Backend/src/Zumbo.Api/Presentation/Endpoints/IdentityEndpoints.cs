using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Messaging;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.Modules.Identity;
using Zumbo.SharedKernel;

using static ApiEndpointResults;

internal static class IdentityEndpoints
{
    internal static IServiceCollection AddIdentityModule(this IServiceCollection services)
    {
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IRefreshSessionStore, RefreshSessionStore>();
        services.AddScoped<IApiKeyStore, ApiKeyStore>();
        services.AddScoped<IRegistrationProvisioningPolicy, RegistrationProvisioningPolicyAdapter>();
        services.AddScoped<ISessionClientContext, SessionClientContextAdapter>();
        services.AddScoped<IPasswordResetNotifier, PasswordResetNotifierAdapter>();
        services.AddSingleton<IMfaSecretProtector, MfaSecretProtectorAdapter>();
        services.AddScoped<IdentityService>();
        services.AddScoped<BrowserSessionService>();
        services.AddScoped<RegisterUserHandler>(provider => new RegisterUserHandler(
            provider.GetRequiredService<IUserRepository>(),
            provider.GetRequiredService<IRefreshSessionStore>(),
            provider.GetRequiredService<IDurableTransactionRunner>(),
            provider.GetRequiredService<IPasswordHasher>(),
            provider.GetRequiredService<ITokenIssuer>(),
            provider.GetRequiredService<IOptions<JwtOptions>>(),
            provider.GetRequiredService<IOptions<IdentityBootstrapOptions>>(),
            provider.GetRequiredService<IDistributedLockProvider>(),
            provider.GetRequiredService<IOptions<DistributedLockOptions>>(),
            provider.GetRequiredService<IClock>(),
            provider.GetRequiredService<IRegistrationProvisioningPolicy>(),
            provider.GetRequiredService<ISessionClientContext>()));
        services.AddScoped<SearchUsersHandler>(provider => new SearchUsersHandler(
            provider.GetRequiredService<IUserRepository>(),
            provider.GetRequiredService<ICurrentUser>()));
        services.AddScoped<ApiKeyService>();
        services.AddScoped<IPrivacyDataProcessor, PrivacyDataProcessorAdapter>();
        services.AddScoped<PrivacyService>();
        services.AddOptions<PrivacyWorkflowOptions>()
            .BindConfiguration("PrivacyWorkflow")
            .Validate(
                options => options.RetentionDays is >= 1 and <= 90
                    && options.LeaseSeconds is >= 5 and <= 3600,
                "Privacy workflow retention or lease settings are outside the supported bounds.")
            .ValidateOnStart();
        services.AddScoped<IPrivacyWorkflowEventPublisher, DurablePrivacyWorkflowEventPublisher>();
        services.AddScoped<PrivacyWorkflowService>();
        services.AddScoped<PrivacyWorkflowProcessor>();
        services.AddScoped<Zumbo.BuildingBlocks.Application.Messaging.IDurableEventHandler,
            PrivacyWorkflowDurableHandler>();
        services.AddScoped<IIdentityAuditWriter, IdentityAuditWriterAdapter>();
        services.AddScoped<IdentityPermissionService>();
        services.AddScoped<IdentityAdministrationService>();
        return services;
    }

    internal static void MapIdentityEndpoints(this RouteGroupBuilder api)
    {
        var group = api.MapGroup("/auth").WithTags("Identity");

        group.MapPost("/register", async (RegisterUserRequest request, RegisterUserHandler handler, HttpContext http, CancellationToken ct) =>
            Ok(await handler.HandleAsync(request, ct), http));

        group.MapPost("/login", async (LoginRequest request, IdentityService service, HttpContext http, CancellationToken ct) =>
            Ok(await service.LoginAsync(request, ct), http))
            .RequireRateLimiting("login");

        group.MapPost("/refresh", async (RefreshTokenRequest request, IdentityService service, HttpContext http, CancellationToken ct) =>
            Ok(await service.RefreshAsync(request, ct), http));

        group.MapPost("/logout", async (LogoutRequest request, IdentityService service, HttpContext http, CancellationToken ct) =>
            Ok(await service.LogoutAsync(request, ct), http));

        group.MapPost("/change-password", async (ChangePasswordRequest request, IdentityService service, HttpContext http, CancellationToken ct) =>
            Ok(await service.ChangePasswordAsync(request, CorrelationId(http), ct), http))
            .RequireAuthorization()
            .WithZumboPermission(PermissionCatalog.ProfileRead);

        group.MapPost("/forgot-password", async (ForgotPasswordRequest request, IdentityService service, HttpContext http, CancellationToken ct) =>
            Ok(await service.ForgotPasswordAsync(request, ct), http))
            .RequireRateLimiting("password-reset");

        group.MapPost("/reset-password", async (ResetPasswordRequest request, IdentityService service, HttpContext http, CancellationToken ct) =>
            Ok(await service.ResetPasswordAsync(request, CorrelationId(http), ct), http))
            .RequireRateLimiting("password-reset");

        group.MapGet("/mfa", async (IdentityService service, HttpContext http, CancellationToken ct) =>
            Ok(await service.GetMfaStatusAsync(ct), http))
            .RequireAuthorization()
            .WithZumboPermission(PermissionCatalog.ProfileRead);

        group.MapPost("/mfa/setup", async (BeginMfaSetupRequest request, IdentityService service, HttpContext http, CancellationToken ct) =>
            Ok(await service.BeginMfaSetupAsync(request, CorrelationId(http), ct), http))
            .RequireAuthorization()
            .WithZumboPermission(PermissionCatalog.ProfileRead);

        group.MapPost("/mfa/confirm", async (ConfirmMfaSetupRequest request, IdentityService service, HttpContext http, CancellationToken ct) =>
            Ok(await service.ConfirmMfaSetupAsync(request, CorrelationId(http), ct), http))
            .RequireAuthorization()
            .WithZumboPermission(PermissionCatalog.ProfileRead);

        group.MapPost("/mfa/disable", async (DisableMfaRequest request, IdentityService service, HttpContext http, CancellationToken ct) =>
            Ok(await service.DisableMfaAsync(request, CorrelationId(http), ct), http))
            .RequireAuthorization()
            .WithZumboPermission(PermissionCatalog.ProfileRead);

        group.MapPost("/mfa/recovery-codes", async (RegenerateMfaRecoveryCodesRequest request, IdentityService service, HttpContext http, CancellationToken ct) =>
            Ok(await service.RegenerateMfaRecoveryCodesAsync(request, CorrelationId(http), ct), http))
            .RequireAuthorization()
            .WithZumboPermission(PermissionCatalog.ProfileRead);

        group.MapGet("/sessions", async (IdentityService service, HttpContext http, CancellationToken ct) =>
            Ok(await service.ListSessionsAsync(http.User.FindFirst("sessionId")?.Value, ct), http))
            .RequireAuthorization()
            .WithZumboPermission(PermissionCatalog.ProfileRead);

        group.MapDelete("/sessions/{sessionId}", async (string sessionId, IdentityService service, HttpContext http, CancellationToken ct) =>
        {
            await service.RevokeSessionAsync(sessionId, CorrelationId(http), ct);
            return Ok(new { revoked = true }, http);
        }).RequireAuthorization()
            .WithZumboPermission(PermissionCatalog.ProfileRead);

        group.MapGet("/api-keys", async (ApiKeyService service, HttpContext http, CancellationToken ct) =>
            Ok(await service.ListAsync(ct), http))
            .RequireAuthorization()
            .WithZumboPermission(PermissionCatalog.ProfileRead);

        group.MapPost("/api-keys", async (CreateApiKeyRequest request, ApiKeyService service, HttpContext http, CancellationToken ct) =>
            Ok(await service.CreateAsync(request, CorrelationId(http), ct), http))
            .RequireAuthorization()
            .WithZumboPermission(PermissionCatalog.ProfileRead);

        group.MapDelete("/api-keys/{apiKeyId}", async (string apiKeyId, ApiKeyService service, HttpContext http, CancellationToken ct) =>
        {
            await service.RevokeAsync(apiKeyId, CorrelationId(http), ct);
            return Ok(new { revoked = true }, http);
        }).RequireAuthorization()
            .WithZumboPermission(PermissionCatalog.ProfileRead);

        group.MapGet("/privacy/export", async (PrivacyService service, HttpContext http, CancellationToken ct) =>
            Ok(await service.ExportAsync(ct), http))
            .RequireAuthorization()
            .WithZumboPermission(PermissionCatalog.ProfileRead);

        group.MapGet("/privacy/export.ndjson", async (
            PrivacyService service,
            HttpContext http,
            CancellationToken ct) =>
        {
            http.Response.StatusCode = StatusCodes.Status200OK;
            http.Response.ContentType = "application/x-ndjson";
            http.Response.Headers.ContentDisposition =
                "attachment; filename=zumbo-privacy-export.ndjson";
            http.Response.Headers.CacheControl = "no-store";
            http.Response.Headers["X-Content-Type-Options"] = "nosniff";
            http.Response.Headers["X-Zumbo-Export-Format"] = "ndjson-v1";
            _ = await service.StreamExportAsync(http.Response.Body, ct);
        }).RequireAuthorization()
            .WithZumboPermission(PermissionCatalog.ProfileRead);

        group.MapPost("/privacy/anonymize", async (AnonymizeAccountRequest request, PrivacyService service, HttpContext http, CancellationToken ct) =>
            Ok(await service.AnonymizeAsync(request, CorrelationId(http), ct), http))
            .RequireAuthorization()
            .WithZumboPermission(PermissionCatalog.ProfileRead)
            .RequireRateLimiting("password-reset");

        group.MapPost("/privacy/anonymization-jobs", async (
            AnonymizeAccountRequest request,
            PrivacyWorkflowService service,
            HttpContext http,
            CancellationToken ct) =>
            Created(await service.SubmitAnonymizationAsync(request, ct), http))
            .RequireAuthorization()
            .WithZumboPermission(PermissionCatalog.ProfileRead)
            .RequireRateLimiting("password-reset");

        group.MapGet("/privacy/jobs/{jobId}", async (
            string jobId,
            PrivacyWorkflowService service,
            HttpContext http,
            CancellationToken ct) =>
            Ok(await service.GetAsync(jobId, ct), http))
            .RequireAuthorization()
            .WithZumboPermission(PermissionCatalog.ProfileRead);

        group.MapGet("/privacy/jobs/{jobId}/status", async (
            string jobId,
            PrivacyWorkflowService service,
            HttpContext http,
            CancellationToken ct) =>
            Ok(await service.GetPublicStatusAsync(
                jobId,
                http.Request.Headers["X-Privacy-Status-Token"].ToString(),
                ct), http))
            .AllowAnonymous();

        group.MapPost("/privacy/jobs/{jobId}/status/recover", async (
            string jobId,
            PrivacyWorkflowService service,
            HttpContext http,
            CancellationToken ct) =>
            Ok(await service.RecoverWithTokenAsync(
                jobId,
                http.Request.Headers["X-Privacy-Status-Token"].ToString(),
                ct), http))
            .AllowAnonymous()
            .RequireRateLimiting("password-reset");

        group.MapDelete("/privacy/jobs/{jobId}/status", async (
            string jobId,
            PrivacyWorkflowService service,
            HttpContext http,
            CancellationToken ct) =>
            Ok(await service.PurgeWithTokenAsync(
                jobId,
                http.Request.Headers["X-Privacy-Status-Token"].ToString(),
                ct), http))
            .AllowAnonymous()
            .RequireRateLimiting("password-reset");

        group.MapPost("/privacy/jobs/{jobId}/retry", async (
            string jobId,
            PrivacyWorkflowService service,
            HttpContext http,
            CancellationToken ct) =>
            Ok(await service.RetryAsync(jobId, ct), http))
            .RequireAuthorization()
            .WithZumboPermission(PermissionCatalog.ProfileRead)
            .RequireRateLimiting("password-reset");

        group.MapPost("/privacy/jobs/{jobId}/reconcile", async (
            string jobId,
            PrivacyWorkflowService service,
            HttpContext http,
            CancellationToken ct) =>
            Ok(await service.ReconcileAsync(jobId, ct), http))
            .RequireAuthorization()
            .WithZumboPermission(PermissionCatalog.ProfileRead)
            .RequireRateLimiting("password-reset");

        group.MapPost("/privacy/jobs/retention/purge", async (
            PrivacyWorkflowService service,
            HttpContext http,
            CancellationToken ct) =>
            Ok(await service.PurgeExpiredAsync(ct), http))
            .RequireAuthorization()
            .WithZumboPermission(PermissionCatalog.ProfileRead);

        group.MapPost("/deactivate", async (DeactivateAccountRequest request, IdentityService service, HttpContext http, CancellationToken ct) =>
            Ok(await service.DeactivateAsync(request, ct), http))
            .RequireAuthorization()
            .WithZumboPermission(PermissionCatalog.ProfileRead);

        group.MapGet("/users", async (string? search, SearchUsersHandler handler, HttpContext http, CancellationToken ct) =>
            Ok(await handler.HandleAsync(new SearchUsersQuery(search), ct), http))
            .RequireAuthorization()
            .WithZumboPermission(PermissionCatalog.ProfileRead);

        group.MapGet("/roles", async (IdentityAdministrationService service, HttpContext http, CancellationToken ct) =>
            Ok(await service.ListRolesAsync(ct), http))
            .RequireAuthorization()
            .WithZumboPermission(PermissionCatalog.ProfileRead);

        group.MapPost("/roles", async (CreateRoleRequest request, IdentityAdministrationService service, HttpContext http, CancellationToken ct) =>
            Created(await service.CreateRoleAsync(request, CorrelationId(http), ct), http))
            .RequireAuthorization()
            .WithZumboPermission(PermissionCatalog.UserRoleManage, isGlobal: true);

        group.MapPut("/roles/{roleId}", async (string roleId, UpdateRoleRequest request, IdentityAdministrationService service, HttpContext http, CancellationToken ct) =>
            Ok(await service.UpdateRoleAsync(roleId, request, CorrelationId(http), ct), http))
            .RequireAuthorization()
            .WithZumboPermission(PermissionCatalog.UserRoleManage, isGlobal: true);

        group.MapDelete("/roles/{roleId}", async (string roleId, IdentityAdministrationService service, HttpContext http, CancellationToken ct) =>
        {
            await service.DeleteRoleAsync(roleId, CorrelationId(http), ct);
            return Ok(new { deleted = true }, http);
        }).RequireAuthorization()
            .WithZumboPermission(PermissionCatalog.UserRoleManage, isGlobal: true);

        group.MapPut("/users/{userId}/roles", async (
            string userId,
            AssignUserRolesRequest request,
            IdentityAdministrationService service,
            HttpContext http,
            CancellationToken ct) =>
            Ok(await service.AssignRolesAsync(userId, request, CorrelationId(http), ct), http))
            .RequireAuthorization()
            .WithZumboPermission(PermissionCatalog.UserRoleManage, isGlobal: true);

        var browser = api.MapGroup("/browser-auth").WithTags("BrowserIdentity");
        browser.MapPost("/register", async (RegisterUserRequest request, BrowserSessionService service, HttpContext http, CancellationToken ct) =>
            Ok(await service.RegisterAsync(request, http, ct), http));
        browser.MapPost("/login", async (LoginRequest request, BrowserSessionService service, HttpContext http, CancellationToken ct) =>
            Ok(await service.LoginAsync(request, http, ct), http))
            .RequireRateLimiting("login");
        browser.MapPost("/refresh", async (BrowserSessionService service, HttpContext http, CancellationToken ct) =>
            Ok(await service.RefreshAsync(http, ct), http));
        browser.MapPost("/logout", async (BrowserLogoutRequest request, BrowserSessionService service, HttpContext http, CancellationToken ct) =>
            Ok(await service.LogoutAsync(request, http, ct), http));
        browser.MapGet("/session", async (BrowserSessionService service, HttpContext http, CancellationToken ct) =>
            Ok(await service.GetSessionAsync(http, ct), http))
            .RequireAuthorization()
            .WithZumboPermission(PermissionCatalog.ProfileRead);
    }
}
