using System.Security.Cryptography;
using System.Text;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Identity;

public sealed class ApiKeyStore(
    IDocumentRepository<ApiKeyDocument> apiKeys) : IApiKeyStore
{
    public async Task CreateAsync(ApiKeyDocument apiKey, CancellationToken ct) =>
        await apiKeys.CreateAsync(apiKey, ct);

    public Task<ApiKeyDocument?> GetByIdAsync(string apiKeyId, CancellationToken ct) =>
        apiKeys.SelectAsync(x => x.Id == apiKeyId, ct);

    public Task<ApiKeyDocument?> GetOwnedAsync(
        string apiKeyId,
        string userId,
        string organizationId,
        CancellationToken ct) =>
        apiKeys.SelectAsync(
            x => x.Id == apiKeyId
                && x.UserId == userId
                && x.OrganizationId == organizationId,
            ct);

    public Task<IReadOnlyList<ApiKeyDocument>> ListOwnedAsync(
        string userId,
        string organizationId,
        CancellationToken ct) =>
        apiKeys.ListByFilterAsync(
            x => x.UserId == userId && x.OrganizationId == organizationId,
            x => x.CreatedAt,
            orderDescending: true,
            pageSize: 100,
            cancellationToken: ct);

    public async Task<IReadOnlyList<ApiKeyDocument>> ListAllOwnedAsync(
        string userId,
        string organizationId,
        CancellationToken ct)
    {
        var result = new List<ApiKeyDocument>();
        string? cursor = null;
        do
        {
            var page = await apiKeys.ListByCursorAsync(
                x => x.UserId == userId && x.OrganizationId == organizationId,
                cursor,
                pageSize: 200,
                cancellationToken: ct);
            result.AddRange(page.Items);
            cursor = page.NextCursor;
        }
        while (cursor is not null);

        return result;
    }

    public async Task<bool> ReplaceOwnedAsync(ApiKeyDocument apiKey, CancellationToken ct)
    {
        var result = await apiKeys.ReplaceByVersionAsync(
            x => x.Id == apiKey.Id
                && x.UserId == apiKey.UserId
                && x.OrganizationId == apiKey.OrganizationId,
            apiKey,
            apiKey.Version,
            ct);
        if (result.Found)
        {
            apiKey.Version = result.Version!.Value;
        }

        return result.Found;
    }

    public async Task<int> PurgeExpiredAsync(DateTimeOffset now, int batchSize, CancellationToken ct)
    {
        var expired = await apiKeys.ListByFilterAsync(
            x => x.ExpiresAtUtc <= now.UtcDateTime,
            x => x.ExpiresAtUtc,
            pageSize: Math.Clamp(batchSize, 1, 500),
            cancellationToken: ct);
        if (expired.Count == 0)
        {
            return 0;
        }

        var ids = expired.Select(x => x.Id).ToHashSet(StringComparer.Ordinal);
        return checked((int)await apiKeys.DeleteByFilterAsync(
            x => ids.Contains(x.Id) && x.ExpiresAtUtc <= now.UtcDateTime,
            ct));
    }
}
