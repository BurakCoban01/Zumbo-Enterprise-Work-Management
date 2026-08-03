using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using System.Linq.Expressions;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Audit;

public sealed class AuditService
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> LocalLocks = new(StringComparer.Ordinal);
    private readonly IDocumentRepository<AuditLogDocument> auditLogs;
    private readonly IClock clock;
    private readonly ICurrentUser currentUser;
    private readonly IAuditRequestContext requestContext;
    private readonly IAuditAccessChecker accessChecker;
    private readonly AuditOptions options;
    private readonly IAuditTenantResolver? tenantResolver;
    private readonly IDistributedLockProvider? distributedLocks;
    private readonly WriteAuditLogHandler writeAuditLogHandler;
    private readonly QueryAuditLogHandler queryAuditLogHandler;

    public AuditService(
        IDocumentRepository<AuditLogDocument> auditLogs,
        IClock clock,
        ICurrentUser currentUser,
        IAuditRequestContext requestContext,
        IAuditAccessChecker accessChecker,
        IOptions<AuditOptions>? options = null,
        IAuditTenantResolver? tenantResolver = null,
        IDistributedLockProvider? distributedLocks = null)
    {
        this.auditLogs = auditLogs;
        this.clock = clock;
        this.currentUser = currentUser;
        this.requestContext = requestContext;
        this.accessChecker = accessChecker;
        this.options = options?.Value ?? new AuditOptions();
        this.tenantResolver = tenantResolver;
        this.distributedLocks = distributedLocks;
        writeAuditLogHandler = new WriteAuditLogHandler(
            auditLogs,
            clock,
            currentUser,
            requestContext,
            Microsoft.Extensions.Options.Options.Create(this.options),
            tenantResolver,
            distributedLocks);
        queryAuditLogHandler = new QueryAuditLogHandler(auditLogs, accessChecker);
    }

    public Task WriteAsync(
        string action,
        string entityType,
        string entityId,
        string? oldValue,
        string? newValue,
        string correlationId,
        CancellationToken ct) =>
        writeAuditLogHandler.HandleUncheckedAsync(
            new WriteAuditLogCommand(
                action,
                entityType,
                entityId,
                oldValue,
                newValue,
                correlationId),
            ct);

    public async Task WriteAsAsync(
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
            throw new InvalidOperationException("Audit writes require organization scope.");

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
                ct)) return;
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

    public async Task<IReadOnlyList<AuditLogResponse>> ListByEntityAsync(
        string entityType,
        string entityId,
        CancellationToken ct) =>
        (await QueryAsync(new AuditLogQuery(null, null, entityType, entityId, null, null, PageSize: 100), ct)).Items;

    public async Task<IReadOnlyList<AuditLogResponse>> ListByUserAsync(
        string actorUserId,
        CancellationToken ct) =>
        (await QueryAsync(new AuditLogQuery(actorUserId, null, null, null, null, null, PageSize: 100), ct)).Items;

    public async Task<AuditLogPageResponse> QueryAsync(AuditLogQuery query, CancellationToken ct)
        => await queryAuditLogHandler.HandleAsync(query, ct);

    public async Task<IReadOnlyList<AuditLogResponse>> ExportAsync(AuditLogQuery query, CancellationToken ct)
    {
        var normalized = QueryAuditLogValidator.ValidateAndNormalize(query with { Page = 1, PageSize = 100, Cursor = null });
        var scope = await accessChecker.EnsureCanReadAsync(normalized, ct);
        var result = new List<AuditLogResponse>();
        string? cursor = null;
        do
        {
            var page = await QueryAsync(normalized with { Cursor = cursor }, ct);
            result.AddRange(page.Items);
            if (result.Count > options.ExportMaxRecords)
                throw new ValidationException($"Audit export exceeds the configured limit of {options.ExportMaxRecords} records.");
            cursor = page.NextCursor;
        } while (cursor is not null);
        return result;
    }

    public static async Task WriteNdjsonAsync(
        IReadOnlyCollection<AuditLogResponse> records,
        Stream destination,
        CancellationToken ct)
    {
        await using var writer = new StreamWriter(
            destination,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            bufferSize: 16 * 1024,
            leaveOpen: true);
        var written = 0;
        foreach (var record in records)
        {
            await writer.WriteLineAsync(JsonSerializer.Serialize(record).AsMemory(), ct);
            written++;
            if (written % 100 == 0) await writer.FlushAsync(ct);
        }
        await writer.FlushAsync(ct);
    }

    public async Task<AuditRetentionResult> PurgeExpiredAsync(
        string organizationId,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var cutoff = now.AddDays(-options.RetentionDays);
        var candidates = await auditLogs.ListByFilterAsync(
            x => x.OrganizationId == organizationId && x.CreatedAt < cutoff,
            x => x.CreatedAt,
            pageSize: options.RetentionBatchSize,
            cancellationToken: ct);
        var deleted = 0;
        foreach (var candidate in candidates)
            deleted += (int)await auditLogs.DeleteByFilterAsync(
                x => x.Id == candidate.Id && x.OrganizationId == organizationId && x.CreatedAt < cutoff,
                ct);
        return new AuditRetentionResult(organizationId, cutoff, deleted);
    }

    public async Task<AuditIntegrityResult> VerifyIntegrityAsync(string organizationId, CancellationToken ct)
    {
        var records = new List<AuditLogDocument>();
        for (var page = 1; records.Count <= options.IntegrityMaxRecords; page++)
        {
            var batch = await auditLogs.ListByFilterAsync(
                x => x.OrganizationId == organizationId && x.RecordHash != null,
                x => x.ChainSequence,
                page: page,
                pageSize: 200,
                cancellationToken: ct);
            records.AddRange(batch);
            if (batch.Count < 200) break;
        }
        if (records.Count > options.IntegrityMaxRecords)
            throw new ValidationException("Audit integrity verification exceeds its configured limit.");
        var first = records.FirstOrDefault();
        var completeHistory = first is null
            || (first.ChainSequence == 1 && first.PreviousHash is null);
        var firstSequence = first?.ChainSequence ?? 0;
        var anchorHash = completeHistory ? null : first?.PreviousHash;
        var expectedSequence = firstSequence;
        string? previousHash = anchorHash;
        foreach (var record in records)
        {
            if (record.ChainSequence != expectedSequence
                || !string.Equals(record.PreviousHash, previousHash, StringComparison.Ordinal)
                || !CryptographicOperations.FixedTimeEquals(
                    Encoding.ASCII.GetBytes(record.RecordHash ?? string.Empty),
                    Encoding.ASCII.GetBytes(ComputeHash(record, options.IntegrityKey))))
                return new AuditIntegrityResult(
                    organizationId, records.Count, false, record.Id,
                    completeHistory, firstSequence, anchorHash);
            previousHash = record.RecordHash;
            expectedSequence++;
        }
        return new AuditIntegrityResult(
            organizationId, records.Count, true, null,
            completeHistory, firstSequence, anchorHash);
    }

    private async Task<IAsyncDisposable> AcquireChainLockAsync(string organizationId, CancellationToken ct)
    {
        if (distributedLocks is not null)
        {
            return await distributedLocks.TryAcquireAsync(
                "audit-chain:" + organizationId,
                TimeSpan.FromSeconds(30),
                TimeSpan.FromSeconds(5),
                ct) ?? throw new ConflictException("AUDIT_CHAIN_BUSY", "Audit chain is busy; retry the operation.");
        }
        var semaphore = LocalLocks.GetOrAdd(organizationId, _ => new SemaphoreSlim(1, 1));
        await semaphore.WaitAsync(ct);
        return new SemaphoreLease(semaphore);
    }

    private static Expression<Func<AuditLogDocument, bool>> BuildFilter(
        AuditLogQuery query,
        string organizationId,
        AuditCursor? cursor) =>
        x => x.OrganizationId == organizationId
            && (query.ActorUserId == null || x.ActorUserId == query.ActorUserId)
            && (query.Action == null || x.Action == query.Action)
            && (query.EntityType == null || x.EntityType == query.EntityType)
            && (query.EntityId == null || x.EntityId == query.EntityId)
            && (query.From == null || x.CreatedAt >= query.From)
            && (query.To == null || x.CreatedAt <= query.To)
            && (cursor == null || x.CreatedAt < cursor.CreatedAt
                || (x.CreatedAt == cursor.CreatedAt && x.Id.CompareTo(cursor.Id) > 0));

    private static string EncodeCursor(AuditLogDocument document) => Convert.ToBase64String(
        Encoding.UTF8.GetBytes($"{document.CreatedAt.UtcTicks}|{document.Id}"));

    private static AuditCursor? DecodeCursor(string? cursor)
    {
        if (cursor is null) return null;
        try
        {
            var parts = Encoding.UTF8.GetString(Convert.FromBase64String(cursor)).Split('|', 2);
            if (parts.Length != 2
                || string.IsNullOrWhiteSpace(parts[1])
                || !long.TryParse(parts[0], out var ticks)
                || ticks < DateTimeOffset.MinValue.UtcTicks
                || ticks > DateTimeOffset.MaxValue.UtcTicks)
                throw new ValidationException("Audit cursor is invalid.");
            return new AuditCursor(new DateTimeOffset(ticks, TimeSpan.Zero), parts[1]);
        }
        catch (Exception exception) when (exception is FormatException or ArgumentException)
        {
            throw new ValidationException("Audit cursor is invalid.");
        }
    }

    private static string ComputeHash(AuditLogDocument document, string key)
    {
        if (string.IsNullOrWhiteSpace(key) || Encoding.UTF8.GetByteCount(key) < 32)
            throw new InvalidOperationException("Audit integrity key must contain at least 32 UTF-8 bytes.");
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

    private static AuditLogResponse ToResponse(AuditLogDocument log) => new(
        log.Id, log.ActorUserId, log.Action, log.EntityType, log.EntityId,
        log.OldValue, log.NewValue, log.IpAddress, log.UserAgent, log.CorrelationId, log.CreatedAt,
        log.OrganizationId, log.SubjectType, log.SubjectId,
        log.Changes.Select(x => new AuditChangeResponse(x.Field, x.OldValue, x.NewValue, x.Redacted)).ToList(),
        log.ChainSequence, log.PreviousHash, log.RecordHash);

    private static string NormalizeRequired(string? value, string fallback, int maximumLength) =>
        NormalizeBounded(value, maximumLength) ?? fallback;

    private static string? NormalizeBounded(string? value, int maximumLength)
    {
        var normalized = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        return normalized?.Length > maximumLength ? normalized[..maximumLength] : normalized;
    }

    private sealed record AuditCursor(DateTimeOffset CreatedAt, string Id);
    private sealed class SemaphoreLease(SemaphoreSlim semaphore) : IAsyncDisposable
    {
        public ValueTask DisposeAsync()
        {
            semaphore.Release();
            return ValueTask.CompletedTask;
        }
    }
}
