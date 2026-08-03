using System.Security.Cryptography;
using System.Text;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Identity;

public interface IApiKeyStore
{
    Task CreateAsync(ApiKeyDocument apiKey, CancellationToken ct);
    Task<ApiKeyDocument?> GetByIdAsync(string apiKeyId, CancellationToken ct);
    Task<ApiKeyDocument?> GetOwnedAsync(
        string apiKeyId,
        string userId,
        string organizationId,
        CancellationToken ct);
    Task<IReadOnlyList<ApiKeyDocument>> ListOwnedAsync(
        string userId,
        string organizationId,
        CancellationToken ct);
    Task<IReadOnlyList<ApiKeyDocument>> ListAllOwnedAsync(
        string userId,
        string organizationId,
        CancellationToken ct);
    Task<bool> ReplaceOwnedAsync(ApiKeyDocument apiKey, CancellationToken ct);
    Task<int> PurgeExpiredAsync(DateTimeOffset now, int batchSize, CancellationToken ct);
}
