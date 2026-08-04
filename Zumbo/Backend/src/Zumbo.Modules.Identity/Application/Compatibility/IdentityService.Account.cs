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
    public async Task<AccountStatusResponse> DeactivateAsync(DeactivateAccountRequest request, CancellationToken ct) =>
            await deactivateAccountHandler.HandleAsync(request, ct);

    public Task<IReadOnlyList<UserProfileResponse>> SearchUsersAsync(string? search, CancellationToken ct) =>
            searchUsersHandler.HandleAsync(new SearchUsersQuery(search), ct);

    private async Task<UserDocument> GetCurrentUserAsync(CancellationToken ct)
        {
            var userId = currentUser.UserId;
            if (string.IsNullOrWhiteSpace(userId))
            {
                throw new UnauthorizedException("Authenticated user is required.");
            }

            return await users.GetByIdAsync(userId, ct)
                ?? throw new UnauthorizedException("Authenticated user was not found.");
        }

    private Task WriteAuditAsync(
            string action,
            string entityId,
            string? oldValue,
            string? newValue,
            string correlationId,
            CancellationToken ct) =>
            audit?.WriteAsync(action, entityId, oldValue, newValue, correlationId, ct)
            ?? Task.CompletedTask;

    private static string? NormalizeDeviceName(string? value)
        {
            var normalized = value?.Trim();
            return string.IsNullOrWhiteSpace(normalized)
                || normalized.Length > 80
                || normalized.Any(char.IsControl)
                    ? null
                    : normalized;
        }
}
