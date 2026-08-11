using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Identity;

public sealed class IdentityPermissionCatalogService(
    IDocumentRepository<IdentityPermissionDefinitionDocument> definitions,
    IDistributedLockProvider distributedLockProvider,
    IOptions<DistributedLockOptions> distributedLockOptions,
    IIdentityAuditWriter audit,
    IClock clock)
{
    public async Task<IReadOnlyList<PermissionDefinitionResponse>> ListAsync(CancellationToken ct)
    {
        await EnsureSeededAsync(ct);
        var result = await definitions.ListByFilterAsync(
            x => x.IsActive,
            x => x.DisplayOrder,
            pageSize: 200,
            cancellationToken: ct);
        return result.Select(ToResponse).ToList();
    }

    public async Task<PermissionDefinitionResponse> UpdateAsync(
        string key,
        UpdatePermissionDefinitionRequest request,
        string correlationId,
        CancellationToken ct)
    {
        await EnsureSeededAsync(ct);
        var normalizedKey = key.Trim();
        var definition = await definitions.SelectAsync(
            x => x.Key.ToLower() == normalizedKey.ToLower(),
            ct) ?? throw new NotFoundException("PERMISSION_DEFINITION_NOT_FOUND", "Permission definition was not found.");
        if (request.Version != definition.Version)
        {
            throw new ConflictException("PERMISSION_DEFINITION_CONFLICT", "Permission metadata changed concurrently; reload and retry.");
        }

        var label = NormalizeText(request.Label, 2, 80, "Permission label");
        var description = NormalizeText(request.Description, 2, 240, "Permission description");
        var category = NormalizeText(request.Category, 2, 60, "Permission category");
        if (request.DisplayOrder is < 0 or > 10000)
        {
            throw new ValidationException("Permission display order must be between 0 and 10000.");
        }

        var oldValue = $"{definition.Label}|{definition.Category}|{definition.DisplayOrder}|{definition.IsActive}";
        definition.Label = label;
        definition.Description = description;
        definition.Category = category;
        definition.DisplayOrder = request.DisplayOrder;
        definition.IsActive = request.IsActive;
        definition.IsCustomized = true;
        definition.UpdatedAt = clock.UtcNow;
        var result = await definitions.ReplaceByVersionAsync(
            x => x.Id == definition.Id,
            definition,
            definition.Version,
            ct);
        if (!result.Found)
        {
            throw new ConflictException("PERMISSION_DEFINITION_CONFLICT", "Permission metadata changed concurrently; reload and retry.");
        }

        definition.Version = result.Version!.Value;
        await audit.WriteAsync(
            "PermissionMetadataUpdated",
            definition.Id,
            oldValue,
            $"{definition.Label}|{definition.Category}|{definition.DisplayOrder}|{definition.IsActive}",
            correlationId,
            ct);
        return ToResponse(definition);
    }

    private async Task EnsureSeededAsync(CancellationToken ct)
    {
        var options = distributedLockOptions.Value;
        await using var catalogLock = await distributedLockProvider.TryAcquireAsync(
            "identity-permission-catalog-seed-v" + IdentityPermissionSeedDefinitions.Version,
            TimeSpan.FromSeconds(Math.Clamp(options.LeaseSeconds, 5, 300)),
            TimeSpan.FromSeconds(Math.Clamp(options.WaitSeconds, 0, 30)),
            ct) ?? throw new ConflictException("IDENTITY_CATALOG_BUSY", "Permission catalog is being initialized; retry the operation.");

        foreach (var seed in IdentityPermissionSeedDefinitions.All)
        {
            var existing = await definitions.SelectAsync(x => x.Key == seed.Key, ct);
            if (existing is null)
            {
                var now = clock.UtcNow;
                await definitions.CreateAsync(new IdentityPermissionDefinitionDocument
                {
                    Id = "permission-" + seed.Key.ToLowerInvariant().Replace('.', '-'),
                    Key = seed.Key,
                    Label = seed.Label,
                    Description = seed.Description,
                    Category = seed.Category,
                    Scope = seed.Scope,
                    DisplayOrder = seed.DisplayOrder,
                    SeedVersion = IdentityPermissionSeedDefinitions.Version,
                    CreatedAt = now,
                    UpdatedAt = now
                }, ct);
                continue;
            }

            if (existing.IsCustomized || existing.SeedVersion >= IdentityPermissionSeedDefinitions.Version)
            {
                continue;
            }

            existing.Label = seed.Label;
            existing.Description = seed.Description;
            existing.Category = seed.Category;
            existing.Scope = seed.Scope;
            existing.DisplayOrder = seed.DisplayOrder;
            existing.SeedVersion = IdentityPermissionSeedDefinitions.Version;
            existing.UpdatedAt = clock.UtcNow;
            await definitions.ReplaceByFilterAsync(x => x.Id == existing.Id, existing, ct);
        }
    }

    private static string NormalizeText(string? value, int minimum, int maximum, string field)
    {
        var normalized = Regex.Replace(value?.Trim() ?? string.Empty, "\\s+", " ");
        if (normalized.Length < minimum || normalized.Length > maximum)
        {
            throw new ValidationException($"{field} must contain {minimum}-{maximum} characters.");
        }

        return normalized;
    }

    private static PermissionDefinitionResponse ToResponse(IdentityPermissionDefinitionDocument definition) =>
        new(
            definition.Key,
            definition.Label,
            definition.Description,
            definition.Category,
            definition.Scope,
            definition.DisplayOrder,
            definition.IsActive,
            definition.Version);
}
