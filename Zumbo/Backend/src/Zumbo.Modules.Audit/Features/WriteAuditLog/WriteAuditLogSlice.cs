using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Audit;

internal sealed class WriteAuditLogSlice(
    IDocumentRepository<AuditLogDocument> auditLogs,
    IClock clock,
    ICurrentUser currentUser,
    IAuditRequestContext requestContext,
    IOptions<AuditOptions> configuredOptions,
    IAuditTenantResolver? tenantResolver,
    IDistributedLockProvider? distributedLocks)
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> LocalLocks =
        new(StringComparer.Ordinal);
    private readonly AuditOptions options = configuredOptions.Value;

    internal Task HandleAsync(WriteAuditLogCommand command, CancellationToken ct) =>
        WriteAsAsync(
            currentUser.UserId ?? "system",
            command.Action,
            command.EntityType,
            command.EntityId,
            command.OldValue,
            command.NewValue,
            command.CorrelationId,
            requestContext.GetMetadata(),
            clock.UtcNow,
            null,
            ct);

    private async Task WriteAsAsync(
        string actorUserId,
        string action,
        string entityType,
        string entityId,
        string? oldValue,
        string? newValue,
        string correlationId,
        AuditRequestMetadata metadata,
        DateTimeOffset occurredAt,
        string? deduplicationKey,
        CancellationToken ct)
    {
        var tenant = tenantResolver is null
            ? new AuditTenant(currentUser.OrganizationId ?? "system", entityType, entityId)
            : await tenantResolver.ResolveAsync(entityType, entityId, actorUserId, ct);
        if (string.IsNullOrWhiteSpace(tenant.OrganizationId))
        {
            throw new InvalidOperationException("Audit writes require organization scope.");
        }

        var normalizedDeduplicationKey = NormalizeBounded(deduplicationKey, 128);
        var diff = AuditDiff.Create(oldValue, newValue);
        var document = new AuditLogDocument
        {
            OrganizationId = tenant.OrganizationId,
            ActorUserId = NormalizeRequired(actorUserId, "system", 128),
            SubjectType = NormalizeRequired(tenant.SubjectType, entityType, 80),
            SubjectId = NormalizeRequired(tenant.SubjectId, entityId, 200),
            Action = NormalizeRequired(action, "Unknown", 120),
            EntityType = NormalizeRequired(entityType, "Unknown", 80),
            EntityId = NormalizeRequired(entityId, "unknown", 200),
            OldValue = diff.OldValue,
            NewValue = diff.NewValue,
            Changes = diff.Changes,
            IpAddress = NormalizeBounded(metadata.IpAddress, 64),
            UserAgent = NormalizeBounded(metadata.UserAgent, 512),
            CorrelationId = NormalizeRequired(correlationId, "none", 128),
            DeduplicationKey = normalizedDeduplicationKey,
            CreatedAt = occurredAt
        };

        if (!options.HashChainEnabled && normalizedDeduplicationKey is null)
        {
            await auditLogs.CreateAsync(document, ct);
            return;
        }

        await using var chainLock = await AcquireChainLockAsync(tenant.OrganizationId, ct);
        if (normalizedDeduplicationKey is not null
            && await auditLogs.ExistsByFilterAsync(
                x => x.OrganizationId == tenant.OrganizationId
                    && x.DeduplicationKey == normalizedDeduplicationKey,
                ct))
        {
            return;
        }

        if (options.HashChainEnabled)
        {
            var previous = (await auditLogs.ListByFilterAsync(
                x => x.OrganizationId == tenant.OrganizationId && x.RecordHash != null,
                x => x.ChainSequence,
                orderDescending: true,
                pageSize: 1,
                cancellationToken: ct)).SingleOrDefault();
            document.ChainSequence = (previous?.ChainSequence ?? 0) + 1;
            document.PreviousHash = previous?.RecordHash;
            document.RecordHash = ComputeHash(document, options.IntegrityKey);
        }

        await auditLogs.CreateAsync(document, ct);
    }

    private async Task<IAsyncDisposable> AcquireChainLockAsync(string organizationId, CancellationToken ct)
    {
        if (distributedLocks is not null)
        {
            return await distributedLocks.TryAcquireAsync(
                "audit-chain:" + organizationId,
                TimeSpan.FromSeconds(30),
                TimeSpan.FromSeconds(5),
                ct) ?? throw new ConflictException(
                    "AUDIT_CHAIN_BUSY", "Audit chain is busy; retry the operation.");
        }

        var semaphore = LocalLocks.GetOrAdd(organizationId, _ => new SemaphoreSlim(1, 1));
        await semaphore.WaitAsync(ct);
        return new SemaphoreLease(semaphore);
    }

    private static string ComputeHash(AuditLogDocument document, string key)
    {
        if (string.IsNullOrWhiteSpace(key) || Encoding.UTF8.GetByteCount(key) < 32)
        {
            throw new InvalidOperationException(
                "Audit integrity key must contain at least 32 UTF-8 bytes.");
        }

        var canonical = JsonSerializer.Serialize(new
        {
            document.SchemaVersion,
            document.OrganizationId,
            document.ActorUserId,
            document.SubjectType,
            document.SubjectId,
            document.Action,
            document.EntityType,
            document.EntityId,
            document.OldValue,
            document.NewValue,
            document.Changes,
            document.IpAddress,
            document.UserAgent,
            document.CorrelationId,
            document.DeduplicationKey,
            CreatedAt = document.CreatedAt.ToUniversalTime().ToString("O"),
            document.ChainSequence,
            document.PreviousHash
        });
        return Convert.ToHexString(HMACSHA256.HashData(
            Encoding.UTF8.GetBytes(key),
            Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
    }

    private static string NormalizeRequired(string? value, string fallback, int maximumLength) =>
        NormalizeBounded(value, maximumLength) ?? fallback;

    private static string? NormalizeBounded(string? value, int maximumLength)
    {
        var normalized = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        return normalized?.Length > maximumLength ? normalized[..maximumLength] : normalized;
    }

    private sealed class SemaphoreLease(SemaphoreSlim semaphore) : IAsyncDisposable
    {
        public ValueTask DisposeAsync()
        {
            semaphore.Release();
            return ValueTask.CompletedTask;
        }
    }
}
