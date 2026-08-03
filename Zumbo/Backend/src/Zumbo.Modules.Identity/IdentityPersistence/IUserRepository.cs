using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Identity;

public interface IUserRepository
{
    Task<UserDocument?> GetByUsernameOrEmailAsync(string usernameOrEmail, CancellationToken ct);
    Task<UserDocument?> GetByIdAsync(string userId, CancellationToken ct);
    Task<UserDocument?> GetByRefreshTokenAsync(string refreshToken, CancellationToken ct);
    Task<UserDocument?> GetByPasswordResetTokenAsync(string token, CancellationToken ct);
    Task<bool> HasSystemAdminAsync(CancellationToken ct);
    Task<IReadOnlyList<UserProfileResponse>> SearchAsync(string? search, string? organizationId, CancellationToken ct);
    Task AddAsync(UserDocument user, CancellationToken ct);
    Task UpdateAsync(UserDocument user, CancellationToken ct);
}
