using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

public sealed class WorkItemBulkJobService(
    IDocumentRepository<WorkItemBulkJobDocument> jobs,
    IDocumentRepository<WorkItemBulkJobItemDocument> items,
    IDocumentRepository<WorkItemDocument> workItems,
    IProjectPermissionChecker permissionChecker,
    IWorkItemBulkJobEventPublisher publisher,
    IWorkItemBulkArtifactStorage artifacts,
    IWorkItemAuditPublisher audit,
    IOptions<WorkItemBulkJobOptions> configuredOptions,
    IClock clock,
    ICurrentUser currentUser)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private WorkItemBulkJobOptions Options => configuredOptions.Value;

    public async Task<WorkItemBulkJobResponse> SubmitImportAsync(
        CreateWorkItemImportJobRequest request,
        string idempotencyKey,
        string correlationId,
        CancellationToken ct)
    {
        var rows = request.Items?.ToList() ?? [];
        ValidateInput(rows.Count, JsonSerializer.SerializeToUtf8Bytes(request, JsonOptions).Length);
        if (rows.Any(x => string.IsNullOrWhiteSpace(x.SourceKey))
            || rows.Select(x => x.SourceKey.Trim()).Distinct(StringComparer.Ordinal).Count() != rows.Count)
        {
            throw new ValidationException("Import source keys must be non-empty and unique.");
        }
        foreach (var row in rows)
        {
            CreateWorkItemValidator.Validate(ToCreateRequest(request.ProjectId, row));
        }

        var job = await CreateJobAsync(
            request.ProjectId, WorkItemBulkJobTypes.Import, null, null, request.DryRun,
            rows.Count, false, idempotencyKey, request, PermissionCatalog.WorkItemCreate, correlationId, ct);
        if (job.ProcessedItems != 0 || job.DispatchSequence != 0)
        {
            return ToResponse(job);
        }

        for (var index = 0; index < rows.Count; index++)
        {
            await CreateItemAsync(job, index, rows[index].SourceKey.Trim(), rows[index], ct);
        }
        return await DispatchNewJobAsync(job, correlationId, ct);
    }

    public async Task<WorkItemBulkJobResponse> SubmitBulkAsync(
        CreateWorkItemBulkJobRequest request,
        string idempotencyKey,
        string correlationId,
        CancellationToken ct)
    {
        var operation = WorkItemBulkOperations.Normalize(request.Operation);
        var ids = request.WorkItemIds?.Select(x => x?.Trim() ?? string.Empty).ToList() ?? [];
        ValidateInput(ids.Count, JsonSerializer.SerializeToUtf8Bytes(request, JsonOptions).Length);
        if (ids.Any(string.IsNullOrWhiteSpace) || ids.Distinct(StringComparer.Ordinal).Count() != ids.Count)
        {
            throw new ValidationException("Bulk work item ids must be non-empty and unique.");
        }
        var value = NormalizeOperationValue(operation, request.Value);
        var permission = operation switch
        {
            WorkItemBulkOperations.Move => PermissionCatalog.WorkItemMove,
            WorkItemBulkOperations.Assign => PermissionCatalog.WorkItemAssign,
            _ => PermissionCatalog.WorkItemDelete
        };
        var job = await CreateJobAsync(
            request.ProjectId, WorkItemBulkJobTypes.Bulk, operation, value, request.DryRun,
            ids.Count, false, idempotencyKey, request with { Operation = operation, Value = value },
            permission, correlationId, ct);
        if (job.ProcessedItems != 0 || job.DispatchSequence != 0)
        {
            return ToResponse(job);
        }

        for (var index = 0; index < ids.Count; index++)
        {
            await CreateItemAsync(job, index, ids[index], ids[index], ct);
        }
        return await DispatchNewJobAsync(job, correlationId, ct);
    }

    public async Task<WorkItemBulkJobResponse> SubmitExportAsync(
        CreateWorkItemExportJobRequest request,
        string idempotencyKey,
        string correlationId,
        CancellationToken ct)
    {
        await EnsurePermissionAsync(request.ProjectId, PermissionCatalog.WorkItemView, ct);
        var count = await workItems.CountByFilterAsync(
            x => x.ProjectId == request.ProjectId && (request.IncludeArchived || !x.Archived), ct);
        if (count > Options.MaxExportItems)
        {
            throw new ValidationException($"Export cannot exceed {Options.MaxExportItems} work items.");
        }
        var job = await CreateJobAsync(
            request.ProjectId, WorkItemBulkJobTypes.Export, null, null, request.DryRun,
            checked((int)count), request.IncludeArchived, idempotencyKey, request,
            PermissionCatalog.WorkItemView, correlationId, ct);
        return job.DispatchSequence == 0
            ? await DispatchNewJobAsync(job, correlationId, ct)
            : ToResponse(job);
    }

    public async Task<WorkItemBulkJobResponse> GetAsync(string jobId, CancellationToken ct) =>
        ToResponse(await GetOwnedAsync(jobId, PermissionCatalog.WorkItemView, ct));

    public async Task<WorkItemBulkJobPage> ListAsync(
        string projectId, int page, int pageSize, CancellationToken ct)
    {
        var authorization = await EnsurePermissionAsync(projectId, PermissionCatalog.WorkItemView, ct);
        var userId = RequireUser();
        var safePage = Math.Max(1, page);
        var safeSize = Math.Clamp(pageSize, 1, 100);
        var filter = (System.Linq.Expressions.Expression<Func<WorkItemBulkJobDocument, bool>>)(x =>
            x.OrganizationId == authorization.OrganizationId
            && x.ProjectId == projectId
            && x.RequestedByUserId == userId);
        var total = await jobs.CountByFilterAsync(filter, ct);
        var result = await jobs.ListByFilterAsync(
            filter, x => x.CreatedAt, true, safePage, safeSize, ct);
        return new WorkItemBulkJobPage(result.Select(ToResponse).ToList(), safePage, safeSize, total);
    }

    public async Task<WorkItemBulkJobResponse> CancelAsync(
        string jobId, string correlationId, CancellationToken ct)
    {
        var job = await GetOwnedAsync(jobId, PermissionCatalog.WorkItemUpdate, ct);
        if (WorkItemBulkJobStates.IsTerminal(job.State))
        {
            throw new ConflictException("WORK_ITEM_BULK_JOB_TERMINAL", "A completed job cannot be cancelled.");
        }
        job.CancelRequested = true;
        if (job.State is WorkItemBulkJobStates.Pending or WorkItemBulkJobStates.Failed)
        {
            job.State = WorkItemBulkJobStates.Cancelled;
            job.CompletedAt = clock.UtcNow;
        }
        job.UpdatedAt = clock.UtcNow;
        await ReplaceAsync(job, ct);
        await audit.WriteAsync("WorkItemBulkJobCancelRequested", "WorkItemBulkJob", job.Id,
            null, job.State, correlationId, ct);
        return ToResponse(job);
    }

    public async Task<WorkItemBulkJobResponse> RetryAsync(
        string jobId, string correlationId, CancellationToken ct)
    {
        var job = await GetOwnedAsync(jobId, PermissionCatalog.WorkItemUpdate, ct);
        if (job.State is not (WorkItemBulkJobStates.Failed or WorkItemBulkJobStates.CompletedWithErrors))
        {
            throw new ConflictException("WORK_ITEM_BULK_JOB_NOT_RETRYABLE", "Only failed jobs can be retried.");
        }
        var failed = await items.ListByFilterAsync(
            x => x.JobId == job.Id && x.State == WorkItemBulkJobItemStates.Failed,
            x => x.ItemIndex, pageSize: Options.MaxInputItems, cancellationToken: ct);
        foreach (var item in failed)
        {
            item.State = WorkItemBulkJobItemStates.Pending;
            item.ErrorCode = null;
            item.ErrorMessage = null;
            item.ProcessedAt = null;
            await ReplaceItemAsync(item, ct);
        }
        job.State = WorkItemBulkJobStates.Pending;
        job.CancelRequested = false;
        job.LastErrorCode = null;
        job.LastErrorMessage = null;
        job.FailedItems = 0;
        job.ProcessedItems = job.SucceededItems;
        job.NextItemIndex = failed.Count == 0 ? job.NextItemIndex : failed.Min(x => x.ItemIndex);
        job.CompletedAt = null;
        job.UpdatedAt = clock.UtcNow;
        job.DispatchSequence++;
        await ReplaceAsync(job, ct);
        await publisher.PublishAsync(ToEvent(job), ct);
        await audit.WriteAsync("WorkItemBulkJobRetried", "WorkItemBulkJob", job.Id,
            "Failed", "Pending", correlationId, ct);
        return ToResponse(job);
    }

    public async Task<WorkItemBulkArtifactFile> OpenArtifactAsync(
        string jobId, bool errors, CancellationToken ct)
    {
        var job = await GetOwnedAsync(jobId, PermissionCatalog.WorkItemView, ct);
        if (ArtifactsExpired(job))
        {
            await ExpireArtifactsAsync(job, ct);
            throw new NotFoundException(
                "WORK_ITEM_BULK_ARTIFACT_EXPIRED",
                "The requested job artifact has expired.");
        }
        var path = errors ? job.ErrorStoragePath : job.ResultStoragePath;
        var name = errors ? job.ErrorFileName : job.ResultFileName;
        var type = errors ? job.ErrorContentType : job.ResultContentType;
        var checksum = errors ? job.ErrorChecksumSha256 : job.ResultChecksumSha256;
        var size = errors ? job.ErrorSizeBytes : job.ResultSizeBytes;
        if (new[] { path, name, type, checksum }.Any(string.IsNullOrWhiteSpace) || size is null)
        {
            throw new NotFoundException("WORK_ITEM_BULK_ARTIFACT_NOT_FOUND", "The requested job artifact was not found.");
        }
        var content = await artifacts.OpenReadAsync(path!, type!, checksum!, Options.MaxArtifactBytes, ct);
        return new WorkItemBulkArtifactFile(content, name!, type!, size.Value);
    }

    private async Task<WorkItemBulkJobDocument> CreateJobAsync<TRequest>(
        string projectId, string type, string? operation, string? operationValue, bool dryRun,
        int totalItems, bool includeArchived, string idempotencyKey, TRequest request,
        string permission, string correlationId, CancellationToken ct)
    {
        var authorization = await EnsurePermissionAsync(projectId, permission, ct);
        var userId = RequireUser();
        var keyHash = Hash(NormalizeIdempotencyKey(idempotencyKey));
        var fingerprint = Hash($"{type}\u001f{operation}\u001f{JsonSerializer.Serialize(request, JsonOptions)}");
        var existing = await jobs.SelectAsync(x =>
            x.OrganizationId == authorization.OrganizationId
            && x.RequestedByUserId == userId
            && x.IdempotencyKeyHash == keyHash, ct);
        if (existing is not null)
        {
            if (existing.RequestFingerprint != fingerprint)
                throw new ConflictException("IDEMPOTENCY_KEY_REUSED", "Idempotency key was already used for a different request.");
            return existing;
        }
        var now = clock.UtcNow;
        var job = new WorkItemBulkJobDocument
        {
            OrganizationId = authorization.OrganizationId,
            ProjectId = projectId,
            RequestedByUserId = userId,
            Type = type,
            Operation = operation,
            OperationValue = operationValue,
            DryRun = dryRun,
            IdempotencyKeyHash = keyHash,
            RequestFingerprint = fingerprint,
            TotalItems = totalItems,
            IncludeArchived = includeArchived,
            CreatedAt = now,
            UpdatedAt = now
        };
        try { await jobs.CreateAsync(job, ct); }
        catch (DocumentConflictException)
        {
            var raced = await jobs.SelectAsync(x => x.OrganizationId == authorization.OrganizationId
                && x.RequestedByUserId == userId && x.IdempotencyKeyHash == keyHash, ct);
            if (raced is null) throw;
            return raced;
        }
        await audit.WriteAsync("WorkItemBulkJobCreated", "WorkItemBulkJob", job.Id,
            null, $"{type}:{totalItems}:dryRun={dryRun}", correlationId, ct);
        return job;
    }

    private async Task<WorkItemBulkJobResponse> DispatchNewJobAsync(
        WorkItemBulkJobDocument job, string correlationId, CancellationToken ct)
    {
        job.DispatchSequence = 1;
        job.UpdatedAt = clock.UtcNow;
        await ReplaceAsync(job, ct);
        await publisher.PublishAsync(ToEvent(job), ct);
        return ToResponse(job);
    }

    private async Task CreateItemAsync<T>(
        WorkItemBulkJobDocument job, int index, string sourceKey, T payload, CancellationToken ct) =>
        await items.CreateAsync(new WorkItemBulkJobItemDocument
        {
            Id = StableItemId(job.Id, index), OrganizationId = job.OrganizationId,
            ProjectId = job.ProjectId, JobId = job.Id, ItemIndex = index,
            SourceKey = sourceKey, PayloadJson = JsonSerializer.Serialize(payload, JsonOptions)
        }, ct);

    private async Task<WorkItemBulkJobDocument> GetOwnedAsync(string id, string permission, CancellationToken ct)
    {
        var job = await jobs.SelectAsync(x => x.Id == id, ct);
        if (job is null || job.RequestedByUserId != RequireUser()
            || job.OrganizationId != currentUser.OrganizationId)
            throw new NotFoundException("WORK_ITEM_BULK_JOB_NOT_FOUND", "Bulk job was not found.");
        await EnsurePermissionAsync(job.ProjectId, permission, ct);
        return job;
    }

    private Task<ProjectResourceAuthorization> EnsurePermissionAsync(string projectId, string permission, CancellationToken ct) =>
        permissionChecker.EnsureCanAsync(RequireUser(), projectId, permission, ct);

    private void ValidateInput(int count, int bytes)
    {
        if (count < 1 || count > Options.MaxInputItems)
            throw new ValidationException($"Bulk jobs require between 1 and {Options.MaxInputItems} items.");
        if (bytes > Options.MaxInputBytes)
            throw new ValidationException($"Bulk job input cannot exceed {Options.MaxInputBytes} bytes.");
    }

    private static string NormalizeOperationValue(string operation, string? value)
    {
        if (operation == WorkItemBulkOperations.Archive) return string.Empty;
        if (string.IsNullOrWhiteSpace(value)) throw new ValidationException("Bulk operation value is required.");
        var result = value.Trim();
        if (result.Length > 200) throw new ValidationException("Bulk operation value is too long.");
        return result;
    }

    private static string NormalizeIdempotencyKey(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Trim().Length > 128)
            throw new ValidationException("Idempotency-Key must contain between 1 and 128 characters.");
        return value.Trim();
    }

    private string RequireUser() => currentUser.UserId
        ?? throw new UnauthorizedException("Authenticated user is required.");

    public static string StableItemId(string jobId, int index) => Hash($"{jobId}\u001f{index}")[..32];
    internal static string StableImportedWorkItemId(string jobId, int index) => Hash($"import\u001f{jobId}\u001f{index}")[..32];
    internal static CreateWorkItemRequest ToCreateRequest(string projectId, WorkItemImportRow row) =>
        new(projectId, row.BoardId, row.Title, row.Type,
            string.IsNullOrWhiteSpace(row.Priority) ? "Medium" : row.Priority.Trim(),
            row.AssigneeUserId, row.DueDate, row.ParentId, row.TeamId, row.CustomFields);
    internal static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    internal static WorkItemBulkJobDueEvent ToEvent(WorkItemBulkJobDocument job) =>
        new(job.OrganizationId, job.ProjectId, job.Id, job.RequestedByUserId, job.DispatchSequence);
    internal WorkItemBulkJobResponse ToResponse(WorkItemBulkJobDocument job) =>
        new(job.Id, job.ProjectId, job.Type, job.Operation, job.DryRun, job.State,
            job.TotalItems, job.ProcessedItems, job.SucceededItems, job.FailedItems,
            job.CancelRequested, job.ResultStoragePath is not null, job.ErrorStoragePath is not null,
            job.LastErrorCode, job.LastErrorMessage, job.CreatedAt, job.StartedAt, job.CompletedAt,
            job.CompletedAt?.AddDays(Options.ArtifactRetentionDays), job.Version);

    private bool ArtifactsExpired(WorkItemBulkJobDocument job) =>
        job.CompletedAt?.AddDays(Options.ArtifactRetentionDays) <= clock.UtcNow;

    private async Task ExpireArtifactsAsync(WorkItemBulkJobDocument job, CancellationToken ct)
    {
        var paths = new[] { job.ResultStoragePath, job.ErrorStoragePath }
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.Ordinal)
            .Select(path => path!)
            .ToArray();
        if (paths.Length == 0) return;

        job.ResultStoragePath = null;
        job.ResultFileName = null;
        job.ResultContentType = null;
        job.ResultChecksumSha256 = null;
        job.ResultSizeBytes = null;
        job.ErrorStoragePath = null;
        job.ErrorFileName = null;
        job.ErrorContentType = null;
        job.ErrorChecksumSha256 = null;
        job.ErrorSizeBytes = null;
        job.UpdatedAt = clock.UtcNow;
        await ReplaceAsync(job, ct);
        foreach (var path in paths)
        {
            await artifacts.DeleteAsync(path, ct);
        }
        await audit.WriteAsync(
            "WorkItemBulkJobArtifactsExpired",
            "WorkItemBulkJob",
            job.Id,
            null,
            job.CompletedAt?.AddDays(Options.ArtifactRetentionDays).ToString("O"),
            $"bulk-job:{job.Id}",
            ct);
    }
    private async Task ReplaceAsync(WorkItemBulkJobDocument job, CancellationToken ct)
    {
        var result = await jobs.ReplaceByVersionAsync(x => x.Id == job.Id, job, job.Version, ct);
        if (!result.Found) throw new ConflictException("WORK_ITEM_BULK_JOB_CONFLICT", "Bulk job changed concurrently; reload and retry.");
        job.Version = result.Version!.Value;
    }
    private async Task ReplaceItemAsync(WorkItemBulkJobItemDocument item, CancellationToken ct)
    {
        var result = await items.ReplaceByVersionAsync(x => x.Id == item.Id, item, item.Version, ct);
        if (!result.Found) throw new ConflictException("WORK_ITEM_BULK_ITEM_CONFLICT", "Bulk job item changed concurrently.");
        item.Version = result.Version!.Value;
    }
}
