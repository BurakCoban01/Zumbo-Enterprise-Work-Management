using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Messaging;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.Modules.Identity.Application.Features.Login;
using Zumbo.Modules.Identity.Application.Features.Logout;
using Zumbo.Modules.Identity.Application.Features.Mfa;
using Zumbo.Modules.Identity.Application.Features.PasswordChange;
using Zumbo.Modules.Identity.Application.Features.PasswordReset;
using Zumbo.Modules.Identity.Application.Features.SessionManagement;
using Zumbo.Modules.Identity.Application.Features.TokenRefresh;
using Zumbo.Modules.Identity.Application.Features.AccountLifecycle;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Identity;

public sealed partial class IdentityService(
    IUserRepository users,
    IRefreshSessionStore sessions,
    IDurableTransactionRunner transactions,
    IPasswordHasher passwordHasher,
    ITokenIssuer tokenIssuer,
    IOptions<JwtOptions> jwtOptions,
    IOptions<LoginSecurityOptions> loginSecurityOptions,
    IOptions<IdentityBootstrapOptions> bootstrapOptions,
    IOptions<PasswordResetOptions> passwordResetOptions,
    IPasswordResetNotifier passwordResetNotifier,
    IMfaSecretProtector mfaSecretProtector,
    IDistributedLockProvider distributedLockProvider,
    IOptions<DistributedLockOptions> distributedLockOptions,
    IClock clock,
    ICurrentUser currentUser,
    IRegistrationProvisioningPolicy? registrationProvisioningPolicy = null,
    ISessionClientContext? sessionClientContext = null,
    IIdentityAuditWriter? audit = null)
{
    private readonly RegisterUserHandler registerUserHandler = new(
        users,
        sessions,
        transactions,
        passwordHasher,
        tokenIssuer,
        jwtOptions,
        bootstrapOptions,
        distributedLockProvider,
        distributedLockOptions,
        clock,
        registrationProvisioningPolicy,
        sessionClientContext);

    private readonly SearchUsersHandler searchUsersHandler = new(users, currentUser);
    private readonly LoginHandler loginHandler = new(
        users,
        sessions,
        transactions,
        passwordHasher,
        tokenIssuer,
        jwtOptions,
        loginSecurityOptions,
        mfaSecretProtector,
        clock,
        sessionClientContext);
    private readonly LogoutHandler logoutHandler = new(users, sessions, transactions, clock);
    private readonly RefreshTokenHandler refreshTokenHandler = new(
        users,
        sessions,
        transactions,
        tokenIssuer,
        jwtOptions,
        clock,
        sessionClientContext);
    private readonly ChangePasswordHandler changePasswordHandler = new(
        users,
        sessions,
        transactions,
        passwordHasher,
        tokenIssuer,
        jwtOptions,
        clock,
        currentUser,
        audit,
        sessionClientContext);
    private readonly ForgotPasswordHandler forgotPasswordHandler = new(
        users,
        passwordResetOptions,
        passwordResetNotifier,
        clock);
    private readonly ResetPasswordHandler resetPasswordHandler = new(
        users,
        sessions,
        transactions,
        passwordHasher,
        clock,
        audit);
    private readonly ListSessionsHandler listSessionsHandler = new(users, sessions, currentUser);
    private readonly RevokeSessionHandler revokeSessionHandler = new(users, sessions, clock, currentUser, audit);
    private readonly GetMfaStatusHandler getMfaStatusHandler = new(users, currentUser);
    private readonly BeginMfaSetupHandler beginMfaSetupHandler = new(
        users,
        passwordHasher,
        mfaSecretProtector,
        clock,
        currentUser,
        audit);
    private readonly ConfirmMfaSetupHandler confirmMfaSetupHandler = new(
        users,
        sessions,
        transactions,
        mfaSecretProtector,
        clock,
        currentUser,
        audit);
    private readonly DisableMfaHandler disableMfaHandler = new(
        users,
        sessions,
        transactions,
        passwordHasher,
        mfaSecretProtector,
        clock,
        currentUser,
        audit);
    private readonly RegenerateMfaRecoveryCodesHandler regenerateMfaRecoveryCodesHandler = new(
        users,
        sessions,
        transactions,
        passwordHasher,
        mfaSecretProtector,
        clock,
        currentUser,
        audit);
    private readonly DeactivateAccountHandler deactivateAccountHandler = new(
        users,
        sessions,
        transactions,
        passwordHasher,
        clock,
        currentUser);
}
