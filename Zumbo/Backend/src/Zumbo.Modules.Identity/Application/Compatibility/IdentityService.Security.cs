using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Messaging;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Identity;

public sealed partial class IdentityService
{
    public Task<AuthResponse> ChangePasswordAsync(ChangePasswordRequest request, CancellationToken ct) =>
            ChangePasswordAsync(request, "system", ct);

        public async Task<AuthResponse> ChangePasswordAsync(
            ChangePasswordRequest request,
            string correlationId,
            CancellationToken ct) =>
            await changePasswordHandler.HandleAsync(request, correlationId, ct);

    private static void GuardPassword(string password)
        {
            if (string.IsNullOrWhiteSpace(password)
                || password.Length < 10
                || !password.Any(char.IsUpper)
                || !password.Any(char.IsLower)
                || !password.Any(char.IsDigit)
                || password.All(char.IsLetterOrDigit))
            {
                throw new ValidationException("Password must be at least 10 characters and include upper-case, lower-case, number and symbol characters.");
            }
        }

    public async Task<PasswordResetRequestedResponse> ForgotPasswordAsync(
            ForgotPasswordRequest request,
            CancellationToken ct) =>
            await forgotPasswordHandler.HandleAsync(request, ct);

    public Task<PasswordResetResponse> ResetPasswordAsync(ResetPasswordRequest request, CancellationToken ct) =>
            ResetPasswordAsync(request, "system", ct);

        public async Task<PasswordResetResponse> ResetPasswordAsync(
            ResetPasswordRequest request,
            string correlationId,
            CancellationToken ct) =>
            await resetPasswordHandler.HandleAsync(request, correlationId, ct);

    public Task<BeginMfaSetupResponse> BeginMfaSetupAsync(BeginMfaSetupRequest request, CancellationToken ct) =>
            BeginMfaSetupAsync(request, "system", ct);

        public async Task<BeginMfaSetupResponse> BeginMfaSetupAsync(
            BeginMfaSetupRequest request,
            string correlationId,
            CancellationToken ct) =>
            await beginMfaSetupHandler.HandleAsync(request, correlationId, ct);

        public Task<ConfirmMfaSetupResponse> ConfirmMfaSetupAsync(
            ConfirmMfaSetupRequest request,
            CancellationToken ct) =>
            ConfirmMfaSetupAsync(request, "system", ct);

        public async Task<ConfirmMfaSetupResponse> ConfirmMfaSetupAsync(
            ConfirmMfaSetupRequest request,
            string correlationId,
            CancellationToken ct) =>
            await confirmMfaSetupHandler.HandleAsync(request, correlationId, ct);

        public Task<MfaStatusResponse> DisableMfaAsync(DisableMfaRequest request, CancellationToken ct) =>
            DisableMfaAsync(request, "system", ct);

        public async Task<MfaStatusResponse> DisableMfaAsync(
            DisableMfaRequest request,
            string correlationId,
            CancellationToken ct) =>
            await disableMfaHandler.HandleAsync(request, correlationId, ct);

        public async Task<RegenerateMfaRecoveryCodesResponse> RegenerateMfaRecoveryCodesAsync(
            RegenerateMfaRecoveryCodesRequest request,
            string correlationId,
            CancellationToken ct) =>
            await regenerateMfaRecoveryCodesHandler.HandleAsync(request, correlationId, ct);

        public async Task<MfaStatusResponse> GetMfaStatusAsync(CancellationToken ct) =>
            await getMfaStatusHandler.HandleAsync(ct);

        private bool ConsumeMfaCode(UserDocument user, string code, DateTimeOffset now)
        {
            if (string.IsNullOrWhiteSpace(user.MfaSecretProtected))
            {
                return false;
            }

            var secret = mfaSecretProtector.Unprotect(user.MfaSecretProtected);
            if (TotpSecurity.Verify(secret, code, now))
            {
                return true;
            }

            var recoveryHash = TotpSecurity.HashRecoveryCode(code);
            var recoveryIndex = user.MfaRecoveryCodeHashes.FindIndex(x =>
                CryptographicOperations.FixedTimeEquals(
                    Convert.FromHexString(x),
                    Convert.FromHexString(recoveryHash)));
            if (recoveryIndex < 0)
            {
                return false;
            }

            user.MfaRecoveryCodeHashes.RemoveAt(recoveryIndex);
            return true;
        }
}
