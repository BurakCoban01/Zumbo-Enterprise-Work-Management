using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

public sealed class WorkItemBulkJobProcessor(
    IDocumentRepository<WorkItemBulkJobDocument> jobs,
    IDocumentRepository<WorkItemBulkJobItemDocument> items,
    IDocumentRepository<WorkItemDocument> workItems,
    WorkItemService workItemService,
    IProjectPermissionChecker permissionChecker,
    IBoardPlacementPolicy boardPolicy,
    IWorkItemTeamPolicy teamPolicy,
    IWorkItemTypeSchemaPolicy typeSchemas,
    IWorkItemBulkJobEventPublisher publisher,
    IWorkItemBulkArtifactStorage artifacts,
    IWorkItemAuditPublisher audit,
    IOptions<WorkItemBulkJobOptions> configuredOptions,
    IClock clock,
    ICurrentUser currentUser)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private WorkItemBulkJobOptions Options => configuredOptions.Value;

    public async Task ProcessAsync(WorkItemBulkJobDueEvent message, CancellationToken ct)
    {
        var job = await jobs.SelectAsync(x => x.Id == message.JobId, ct);
        if (job is null || job.OrganizationId != message.OrganizationId
            || job.ProjectId != message.ProjectId || job.RequestedByUserId != message.RequestedByUserId)
            throw new ConflictException("WORK_ITEM_BULK_EVENT_INVALID", "Bulk job event ownership is invalid.");
        if (job.DispatchSequence != message.DispatchSequence
            || WorkItemBulkJobStates.IsTerminal(job.State)) return;
        if (currentUser.UserId != job.RequestedByUserId || currentUser.OrganizationId != job.OrganizationId)
            throw new ConflictException("WORK_ITEM_BULK_ACTOR_INVALID", "Bulk job actor context is invalid.");

        try
        {
            await permissionChecker.EnsureCanAsync(
                job.RequestedByUserId, job.ProjectId, RequiredPermission(job), ct);
            if (job.CancelRequested)
            {
                await MarkCancelledAsync(job, ct);
                return;
            }
            if (job.State == WorkItemBulkJobStates.Pending)
            {
                job.State = WorkItemBulkJobStates.Running;
                job.StartedAt ??= clock.UtcNow;
                job.UpdatedAt = clock.UtcNow;
                await ReplaceJobAsync(job, ct);
            }

            if (job.Type == WorkItemBulkJobTypes.Export)
            {
                await ProcessExportAsync(job, ct);
                return;
            }

            await ProcessItemBatchAsync(job, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception exception)
        {
            await MarkFailedAsync(job, exception, ct);
        }
    }

    private async Task ProcessItemBatchAsync(WorkItemBulkJobDocument job, CancellationToken ct)
    {
        var pending = await items.ListByFilterAsync(
            x => x.JobId == job.Id && x.State == WorkItemBulkJobItemStates.Pending,
            x => x.ItemIndex, pageSize: Math.Clamp(Options.BatchSize, 1, 200), cancellationToken: ct);
        foreach (var item in pending)
        {
            ct.ThrowIfCancellationRequested();
            var latest = await jobs.SelectAsync(x => x.Id == job.Id, ct) ?? job;
            if (latest.CancelRequested)
            {
                await MarkCancelledAsync(latest, ct);
                return;
            }
            try
            {
                item.ResultReference = await ProcessItemAsync(job, item, ct);
                item.State = WorkItemBulkJobItemStates.Succeeded;
                item.ErrorCode = null;
                item.ErrorMessage = null;
            }
            catch (ZumboException exception)
            {
                item.State = WorkItemBulkJobItemStates.Failed;
                item.ErrorCode = exception.Code;
                item.ErrorMessage = Limit(exception.Message, 500);
            }
            item.Attempts++;
            item.ProcessedAt = clock.UtcNow;
            await ReplaceItemAsync(item, ct);
        }

        await RefreshProgressAsync(job, ct);
        var next = await items.ListByFilterAsync(
            x => x.JobId == job.Id && x.State == WorkItemBulkJobItemStates.Pending,
            x => x.ItemIndex, pageSize: 1, cancellationToken: ct);
        if (next.Count == 0)
        {
            await CompleteItemJobAsync(job, ct);
            return;
        }
        job.NextItemIndex = next[0].ItemIndex;
        job.DispatchSequence++;
        job.UpdatedAt = clock.UtcNow;
        await ReplaceJobAsync(job, ct);
        await publisher.PublishAsync(WorkItemBulkJobService.ToEvent(job), ct);
    }

    private async Task<string?> ProcessItemAsync(
        WorkItemBulkJobDocument job, WorkItemBulkJobItemDocument item, CancellationToken ct)
    {
        if (job.Type == WorkItemBulkJobTypes.Import)
        {
            var row = Deserialize<WorkItemImportRow>(item.PayloadJson);
            var request = WorkItemBulkJobService.ToCreateRequest(job.ProjectId, row);
            await ValidateImportAsync(request, ct);
            var targetId = WorkItemBulkJobService.StableImportedWorkItemId(job.Id, item.ItemIndex);
            var existing = await workItems.SelectAsync(x => x.Id == targetId, ct);
            if (existing is not null)
            {
                if (existing.ProjectId != job.ProjectId)
                    throw new ConflictException("WORK_ITEM_IMPORT_ID_CONFLICT", "Imported work item identity is already in use.");
                return existing.Id;
            }
            if (job.DryRun) return "validated";
            var created = await workItemService.CreateAsync(
                request, $"bulk-job:{job.Id}:{item.ItemIndex}", ct, targetId);
            return created.Id;
        }

        var workItemId = Deserialize<string>(item.PayloadJson);
        var target = await workItems.SelectAsync(x => x.Id == workItemId, ct)
            ?? throw new NotFoundException("WORK_ITEM_NOT_FOUND", "Work item was not found.");
        if (target.ProjectId != job.ProjectId)
            throw new NotFoundException("WORK_ITEM_NOT_FOUND", "Work item was not found.");
        if (job.DryRun) return target.Id;
        var correlationId = $"bulk-job:{job.Id}:{item.ItemIndex}";
        switch (job.Operation)
        {
            case WorkItemBulkOperations.Move:
                if (target.Status != job.OperationValue)
                    await workItemService.MoveAsync(target.Id, new MoveWorkItemRequest(job.OperationValue!), correlationId, ct);
                break;
            case WorkItemBulkOperations.Assign:
                if (target.AssigneeUserId != job.OperationValue)
                    await workItemService.AssignAsync(target.Id, new AssignWorkItemRequest(job.OperationValue!), correlationId, ct);
                break;
            case WorkItemBulkOperations.Archive:
                if (!target.Archived) await workItemService.ArchiveAsync(target.Id, correlationId, ct);
                break;
            default:
                throw new ValidationException("Bulk job operation is invalid.");
        }
        return target.Id;
    }

    private async Task ValidateImportAsync(CreateWorkItemRequest request, CancellationToken ct)
    {
        CreateWorkItemValidator.Validate(request);
        await boardPolicy.ResolveInitialAsync(request.ProjectId, request.BoardId, ct);
        await typeSchemas.ValidateAsync(request.ProjectId, request.Type, request.CustomFields, ct);
        if (!string.IsNullOrWhiteSpace(request.TeamId))
            await teamPolicy.EnsureCanAssignAsync(request.ProjectId, request.TeamId, request.AssigneeUserId, ct);
        if (!string.IsNullOrWhiteSpace(request.ParentId))
        {
            var parent = await workItems.SelectAsync(x => x.Id == request.ParentId && !x.Archived, ct);
            if (parent is null || parent.ProjectId != request.ProjectId || parent.BoardId != request.BoardId)
                throw new ValidationException("Import parent must be an active work item on the same project and board.");
        }
    }

    private async Task ProcessExportAsync(WorkItemBulkJobDocument job, CancellationToken ct)
    {
        if (job.DryRun)
        {
            job.ProcessedItems = job.TotalItems;
            job.SucceededItems = job.TotalItems;
            await CompleteJobAsync(job, Array.Empty<WorkItemExportRow>(), ct);
            return;
        }
        var rows = new List<WorkItemExportRow>(job.TotalItems);
        string? cursor = null;
        do
        {
            if ((await jobs.SelectAsync(x => x.Id == job.Id, ct))?.CancelRequested == true)
            {
                await MarkCancelledAsync(job, ct);
                return;
            }
            var page = await workItems.ListByCursorAsync(
                x => x.ProjectId == job.ProjectId && (job.IncludeArchived || !x.Archived),
                cursor, Math.Min(200, Math.Max(1, Options.MaxExportItems - rows.Count)), ct);
            rows.AddRange(page.Items.Select(ToExportRow));
            cursor = page.NextCursor;
            job.ProcessedItems = rows.Count;
            job.SucceededItems = rows.Count;
            job.UpdatedAt = clock.UtcNow;
            await ReplaceJobAsync(job, ct);
            if (rows.Count >= Options.MaxExportItems && cursor is not null)
                throw new ValidationException("Export exceeded its configured item limit.");
        } while (cursor is not null);
        await CompleteJobAsync(job, rows, ct);
    }

    private async Task CompleteItemJobAsync(WorkItemBulkJobDocument job, CancellationToken ct)
    {
        var all = await items.ListByFilterAsync(
            x => x.JobId == job.Id, x => x.ItemIndex,
            pageSize: Options.MaxInputItems, cancellationToken: ct);
        await ReplaceArtifactAsync(job, all.Select(x => new
        {
            x.ItemIndex, x.SourceKey, x.State, x.ResultReference, x.ErrorCode, x.ErrorMessage, x.Attempts
        }), false, ct);
        if (job.FailedItems > 0)
        {
            await ReplaceArtifactAsync(job,
                all.Where(x => x.State == WorkItemBulkJobItemStates.Failed)
                .Select(x => new { x.ItemIndex, x.SourceKey, x.ErrorCode, x.ErrorMessage }), true, ct);
        }
        await FinalizeJobAsync(job, ct);
    }

    private async Task CompleteJobAsync<T>(WorkItemBulkJobDocument job, IEnumerable<T> result, CancellationToken ct)
    {
        await ReplaceArtifactAsync(job, result, false, ct);
        await FinalizeJobAsync(job, ct);
    }

    private async Task FinalizeJobAsync(WorkItemBulkJobDocument job, CancellationToken ct)
    {
        job.State = job.FailedItems == 0
            ? WorkItemBulkJobStates.Completed : WorkItemBulkJobStates.CompletedWithErrors;
        job.CompletedAt = clock.UtcNow;
        job.UpdatedAt = clock.UtcNow;
        await ReplaceJobAsync(job, ct);
        await audit.WriteAsync("WorkItemBulkJobCompleted", "WorkItemBulkJob", job.Id,
            "Running", $"{job.State}:{job.SucceededItems}/{job.TotalItems}", $"bulk-job:{job.Id}", ct);
    }

    private async Task ReplaceArtifactAsync<T>(
        WorkItemBulkJobDocument job, IEnumerable<T> rows, bool errors, CancellationToken ct)
    {
        var oldPath = errors ? job.ErrorStoragePath : job.ResultStoragePath;
        if (!string.IsNullOrWhiteSpace(oldPath)) await artifacts.DeleteAsync(oldPath, ct);
        await using var stream = new MemoryStream();
        await using (var writer = new StreamWriter(stream, new UTF8Encoding(false), 16 * 1024, true))
        {
            foreach (var row in rows)
                await writer.WriteLineAsync(JsonSerializer.Serialize(row, JsonOptions).AsMemory(), ct);
        }
        stream.Position = 0;
        var suffix = errors ? "errors" : "result";
        var stored = await artifacts.SaveAsync(stream, $"work-item-job-{job.Id}-{suffix}.ndjson",
            "application/x-ndjson", Options.MaxArtifactBytes, ct);
        if (errors)
        {
            job.ErrorStoragePath = stored.StoragePath; job.ErrorFileName = stored.FileName;
            job.ErrorContentType = stored.ContentType; job.ErrorChecksumSha256 = stored.ChecksumSha256;
            job.ErrorSizeBytes = stored.SizeBytes;
        }
        else
        {
            job.ResultStoragePath = stored.StoragePath; job.ResultFileName = stored.FileName;
            job.ResultContentType = stored.ContentType; job.ResultChecksumSha256 = stored.ChecksumSha256;
            job.ResultSizeBytes = stored.SizeBytes;
        }
    }

    private async Task RefreshProgressAsync(WorkItemBulkJobDocument job, CancellationToken ct)
    {
        job.SucceededItems = checked((int)await items.CountByFilterAsync(
            x => x.JobId == job.Id && x.State == WorkItemBulkJobItemStates.Succeeded, ct));
        job.FailedItems = checked((int)await items.CountByFilterAsync(
            x => x.JobId == job.Id && x.State == WorkItemBulkJobItemStates.Failed, ct));
        job.ProcessedItems = job.SucceededItems + job.FailedItems;
        job.UpdatedAt = clock.UtcNow;
        await ReplaceJobAsync(job, ct);
    }

    private async Task MarkCancelledAsync(WorkItemBulkJobDocument job, CancellationToken ct)
    {
        job.State = WorkItemBulkJobStates.Cancelled; job.CancelRequested = true;
        job.CompletedAt = clock.UtcNow; job.UpdatedAt = clock.UtcNow;
        await ReplaceJobAsync(job, ct);
    }

    private async Task MarkFailedAsync(WorkItemBulkJobDocument job, Exception exception, CancellationToken ct)
    {
        var latest = await jobs.SelectAsync(x => x.Id == job.Id, ct) ?? job;
        if (latest.CancelRequested)
        {
            await MarkCancelledAsync(latest, ct);
            return;
        }
        latest.State = WorkItemBulkJobStates.Failed;
        latest.LastErrorCode = exception is ZumboException zumbo ? zumbo.Code : "WORK_ITEM_BULK_JOB_FAILED";
        latest.LastErrorMessage = exception is ZumboException ? Limit(exception.Message, 500) : "Bulk job processing failed; retry is available.";
        latest.UpdatedAt = clock.UtcNow;
        await ReplaceJobAsync(latest, ct);
    }

    private static string RequiredPermission(WorkItemBulkJobDocument job) => job.Type switch
    {
        WorkItemBulkJobTypes.Import => PermissionCatalog.WorkItemCreate,
        WorkItemBulkJobTypes.Export => PermissionCatalog.WorkItemView,
        _ when job.Operation == WorkItemBulkOperations.Move => PermissionCatalog.WorkItemMove,
        _ when job.Operation == WorkItemBulkOperations.Assign => PermissionCatalog.WorkItemAssign,
        _ => PermissionCatalog.WorkItemDelete
    };
    private static WorkItemExportRow ToExportRow(WorkItemDocument x) =>
        new(x.Id, x.BoardId, x.Title, x.Description, x.Type, x.Priority, x.Status,
            x.AssigneeUserId, x.DueDate, x.ParentId, x.TeamId, x.Labels, x.CustomFields, x.Archived, x.Version);
    private static T Deserialize<T>(string json) => JsonSerializer.Deserialize<T>(json, JsonOptions)
        ?? throw new ValidationException("Bulk job item payload is invalid.");
    private static string Limit(string value, int max) => value.Length <= max ? value : value[..max];
    private async Task ReplaceJobAsync(WorkItemBulkJobDocument job, CancellationToken ct)
    {
        var result = await jobs.ReplaceByVersionAsync(x => x.Id == job.Id, job, job.Version, ct);
        if (!result.Found) throw new ConflictException("WORK_ITEM_BULK_JOB_CONFLICT", "Bulk job changed concurrently.");
        job.Version = result.Version!.Value;
    }
    private async Task ReplaceItemAsync(WorkItemBulkJobItemDocument item, CancellationToken ct)
    {
        var result = await items.ReplaceByVersionAsync(x => x.Id == item.Id, item, item.Version, ct);
        if (!result.Found) throw new ConflictException("WORK_ITEM_BULK_ITEM_CONFLICT", "Bulk job item changed concurrently.");
        item.Version = result.Version!.Value;
    }
}
