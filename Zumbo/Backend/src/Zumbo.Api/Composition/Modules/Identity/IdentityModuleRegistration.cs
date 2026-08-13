using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Messaging;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.Modules.Identity;
using Zumbo.Modules.Identity.Application.Features.AccountLifecycle;
using Zumbo.Modules.Identity.Application.Features.Login;
using Zumbo.Modules.Identity.Application.Features.Logout;
using Zumbo.Modules.Identity.Application.Features.Mfa;
using Zumbo.Modules.Identity.Application.Features.PasswordChange;
using Zumbo.Modules.Identity.Application.Features.PasswordReset;
using Zumbo.Modules.Identity.Application.Features.SessionManagement;
using Zumbo.Modules.Identity.Application.Features.TokenRefresh;
using Zumbo.SharedKernel;

internal static class IdentityModuleRegistration
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
        services.AddScoped<LoginHandler>();
        services.AddScoped<LogoutHandler>();
        services.AddScoped<GetMfaStatusHandler>();
        services.AddScoped<BeginMfaSetupHandler>();
        services.AddScoped<ConfirmMfaSetupHandler>();
        services.AddScoped<DisableMfaHandler>();
        services.AddScoped<RegenerateMfaRecoveryCodesHandler>();
        services.AddScoped<DeactivateAccountHandler>();
        services.AddScoped<ChangePasswordHandler>();
        services.AddScoped<ForgotPasswordHandler>();
        services.AddScoped<ResetPasswordHandler>();
        services.AddScoped<ListSessionsHandler>();
        services.AddScoped<RevokeSessionHandler>();
        services.AddScoped<RefreshTokenHandler>();
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
        services.AddScoped<IDurableEventHandler, PrivacyWorkflowDurableHandler>();
        services.AddScoped<IIdentityAuditWriter, IdentityAuditWriterAdapter>();
        services.AddScoped<IdentityPermissionService>();
        services.AddScoped<IdentityPermissionCatalogService>();
        services.AddScoped<IdentityRoleCatalogService>();
        services.AddScoped<IdentityAdministrationService>();
        return services;
    }
}
