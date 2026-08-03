using System.Security.Cryptography;
using System.Text;
using System.Linq.Expressions;
using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

public sealed class WorkItemCollaborationService(
    IDocumentRepository<WorkItemDocument> workItems,
    IDocumentRepository<WorkItemCollaborationDocument> collaborations,
    IDocumentRepository<WorkItemEventActivityDocument> activities,
    IProjectPermissionChecker permissionChecker,
    IWorkItemCollaboratorDirectory collaboratorDirectory,
    IWorkItemNotificationPublisher notifications,
    IWorkItemAuditPublisher audit,
    IDistributedLockProvider distributedLocks,
    IOptions<DistributedLockOptions> lockOptions,
    IClock clock,
    ICurrentUser currentUser)
{
    private const int WatcherLimit = 200;
    private const int VoterLimit = 1_000;

    public async Task<WorkItemCollaborationResponse> GetAsync(string workItemId, CancellationToken ct)
    {
        var authorized = await GetAuthorizedAsync(workItemId, PermissionCatalog.WorkItemView, ct);
        var collaboration = await GetOrDefaultAsync(authorized, ct);
        return ToResponse(collaboration, RequireCurrentUser());
    }

    public Task<WorkItemCollaborationResponse> SetWatchingAsync(
        string workItemId,
        bool watching,
        string correlationId,
        CancellationToken ct) =>
        MutateAsync(workItemId, watching, isWatcher: true, correlationId, ct);

    public Task<WorkItemCollaborationResponse> SetVoteAsync(
        string workItemId,
        bool voted,
        string correlationId,
        CancellationToken ct) =>
        MutateAsync(workItemId, voted, isWatcher: false, correlationId, ct);

    public async Task<IReadOnlyCollection<string>> ValidateMentionsAsync(
        string organizationId,
        string projectId,
        IReadOnlyCollection<string> userIds,
        CancellationToken ct)
    {
        foreach (var userId in userIds)
        {
            if (!await collaboratorDirectory.IsActiveProjectViewerAsync(
                    userId,
                    organizationId,
                    projectId,
                    ct))
            {
                throw new ValidationException(
                    "Mentioned users must be active users who can view this project.");
            }
        }

        return userIds;
    }

    public async Task NotifyWatchersAsync(
        WorkItemDocument workItem,
        string organizationId,
        string type,
        string message,
        string eventId,
        IReadOnlyCollection<string>? excludedUserIds,
        CancellationToken ct)
    {
        var collaboration = await collaborations.SelectAsync(
            item => item.Id == workItem.Id
                && item.ProjectId == workItem.ProjectId
                && item.OrganizationId == organizationId,
            ct);
        if (collaboration is null)
        {
            return;
        }

        var excluded = (excludedUserIds ?? [])
            .Append(currentUser.UserId ?? string.Empty)
            .ToHashSet(StringComparer.Ordinal);
        foreach (var watcherId in collaboration.WatcherUserIds
                     .Where(id => !excluded.Contains(id))
                     .Distinct(StringComparer.Ordinal))
        {
            await notifications.NotifyAsync(
                watcherId,
                type,
                message,
                ct,
                StableId("watcher", workItem.Id, type, eventId, watcherId));
        }
    }

    public async Task RecordActivityAsync(
        WorkItemDocument workItem,
        string organizationId,
        string type,
        string detail,
        string eventId,
        CancellationToken ct)
    {
        var activityId = StableId("activity", workItem.Id, type, eventId);
        if (await activities.ExistsByFilterAsync(item => item.Id == activityId, ct))
        {
            return;
        }

        await activities.CreateAsync(new WorkItemEventActivityDocument
        {
            Id = activityId,
            OrganizationId = organizationId,
            ProjectId = workItem.ProjectId,
            WorkItemId = workItem.Id,
            Type = NormalizeActivityType(type),
            ActorUserId = currentUser.UserId ?? "system",
            Detail = NormalizeDetail(detail),
            CreatedAt = clock.UtcNow
        }, ct);
    }

    public async Task<WorkItemEventActivityPage> ListActivityAsync(
        string workItemId,
        int page,
        int pageSize,
        CancellationToken ct)
    {
        var authorized = await GetAuthorizedAsync(workItemId, PermissionCatalog.WorkItemView, ct);
        var safePage = Math.Max(page, 1);
        var safeSize = Math.Clamp(pageSize, 1, 100);
        Expression<Func<WorkItemEventActivityDocument, bool>> filter = activity =>
            activity.OrganizationId == authorized.OrganizationId
            && activity.ProjectId == authorized.Item.ProjectId
            && activity.WorkItemId == authorized.Item.Id;
        var total = await activities.CountByFilterAsync(filter, ct);
        var result = await activities.ListByFilterAsync(
            filter,
            item => item.CreatedAt,
            orderDescending: true,
            page: safePage,
            pageSize: safeSize,
            cancellationToken: ct);
        return new WorkItemEventActivityPage(
            result.Select(item => new WorkItemEventActivityResponse(
                item.Id,
                item.Type,
                item.ActorUserId,
                item.Detail,
                item.CreatedAt)).ToList(),
            safePage,
            safeSize,
            total);
    }

    private async Task<WorkItemCollaborationResponse> MutateAsync(
        string workItemId,
        bool enabled,
        bool isWatcher,
        string correlationId,
        CancellationToken ct)
    {
        var authorized = await GetAuthorizedAsync(workItemId, PermissionCatalog.WorkItemView, ct);
        var userId = RequireCurrentUser();
        await using var collaborationLock = await AcquireLockAsync("work-item-collaboration:" + workItemId, ct);
        var existing = await collaborations.SelectAsync(item => item.Id == workItemId, ct);
        var collaboration = existing ?? new WorkItemCollaborationDocument
        {
            Id = workItemId,
            OrganizationId = authorized.OrganizationId,
            ProjectId = authorized.Item.ProjectId,
            WorkItemId = authorized.Item.Id
        };
        EnsureOwnership(collaboration, authorized);
        var users = isWatcher ? collaboration.WatcherUserIds : collaboration.VoterUserIds;
        var changed = enabled ? Add(users, userId) : users.Remove(userId);
        if (!changed)
        {
            throw new ConflictException(
                isWatcher ? "WORK_ITEM_WATCH_UNCHANGED" : "WORK_ITEM_VOTE_UNCHANGED",
                isWatcher
                    ? "The work item watch state is unchanged."
                    : "The work item vote state is unchanged.");
        }

        if (collaboration.WatcherUserIds.Count > WatcherLimit)
        {
            throw new ConflictException("WORK_ITEM_WATCHER_LIMIT", "The work item watcher limit has been reached.");
        }
        if (collaboration.VoterUserIds.Count > VoterLimit)
        {
            throw new ConflictException("WORK_ITEM_VOTER_LIMIT", "The work item voter limit has been reached.");
        }

        collaboration.UpdatedAt = clock.UtcNow;
        if (existing is null)
        {
            collaboration = await collaborations.CreateAsync(collaboration, ct);
        }
        else
        {
            var result = await collaborations.ReplaceByVersionAsync(
                item => item.Id == collaboration.Id,
                collaboration,
                collaboration.Version,
                ct);
            if (!result.Found)
            {
                throw new ConflictException(
                    "WORK_ITEM_COLLABORATION_CONFLICT",
                    "Work item collaboration changed concurrently; retry the operation.");
            }
            collaboration.Version = result.Version!.Value;
        }

        var action = isWatcher
            ? enabled ? "WorkItemWatched" : "WorkItemUnwatched"
            : enabled ? "WorkItemVoted" : "WorkItemVoteRemoved";
        await RecordActivityAsync(
            authorized.Item,
            authorized.OrganizationId,
            action,
            isWatcher
                ? enabled ? "Watch enabled" : "Watch disabled"
                : enabled ? "Vote added" : "Vote removed",
            correlationId,
            ct);
        await audit.WriteAsync(action, "WorkItem", workItemId, null, userId, correlationId, ct);
        if (!isWatcher)
        {
            await NotifyWatchersAsync(
                authorized.Item,
                authorized.OrganizationId,
                "Vote",
                enabled
                    ? $"A vote was added to {authorized.Item.Title}"
                    : $"A vote was removed from {authorized.Item.Title}",
                correlationId,
                [userId],
                ct);
        }

        return ToResponse(collaboration, userId);
    }

    private async Task<AuthorizedWorkItem> GetAuthorizedAsync(
        string workItemId,
        string permission,
        CancellationToken ct)
    {
        var userId = RequireCurrentUser();
        var item = await workItems.SelectAsync(candidate => candidate.Id == workItemId && !candidate.Archived, ct)
            ?? throw new NotFoundException("WORK_ITEM_NOT_FOUND", "Work item was not found.");
        var authorization = await permissionChecker.EnsureCanAsync(userId, item.ProjectId, permission, ct);
        return new AuthorizedWorkItem(item, authorization.OrganizationId);
    }

    private async Task<WorkItemCollaborationDocument> GetOrDefaultAsync(
        AuthorizedWorkItem authorized,
        CancellationToken ct) =>
        await collaborations.SelectAsync(item => item.Id == authorized.Item.Id, ct)
        ?? new WorkItemCollaborationDocument
        {
            Id = authorized.Item.Id,
            OrganizationId = authorized.OrganizationId,
            ProjectId = authorized.Item.ProjectId,
            WorkItemId = authorized.Item.Id
        };

    private async Task<IAsyncDisposable> AcquireLockAsync(string resource, CancellationToken ct)
    {
        var options = lockOptions.Value;
        return await distributedLocks.TryAcquireAsync(
            resource,
            TimeSpan.FromSeconds(Math.Clamp(options.LeaseSeconds, 5, 300)),
            TimeSpan.FromSeconds(Math.Clamp(options.WaitSeconds, 0, 30)),
            ct)
            ?? throw new ConflictException("RESOURCE_BUSY", "The requested resource is busy; retry the operation.");
    }

    private static bool Add(ICollection<string> values, string value)
    {
        if (values.Contains(value, StringComparer.Ordinal))
        {
            return false;
        }
        values.Add(value);
        return true;
    }

    private static void EnsureOwnership(
        WorkItemCollaborationDocument collaboration,
        AuthorizedWorkItem authorized)
    {
        if (collaboration.OrganizationId != authorized.OrganizationId
            || collaboration.ProjectId != authorized.Item.ProjectId
            || collaboration.WorkItemId != authorized.Item.Id)
        {
            throw new ConflictException(
                "WORK_ITEM_COLLABORATION_OWNERSHIP_INVALID",
                "Stored collaboration ownership does not match the work item.");
        }
    }

    private string RequireCurrentUser() =>
        currentUser.UserId ?? throw new UnauthorizedException("Authenticated user is required.");

    private static string NormalizeActivityType(string value)
    {
        var normalized = value.Trim();
        if (normalized.Length is < 1 or > 80)
        {
            throw new ValidationException("Activity type must contain 1 to 80 characters.");
        }
        return normalized;
    }

    private static string NormalizeDetail(string value)
    {
        var normalized = value.Trim();
        if (normalized.Length > 500)
        {
            throw new ValidationException("Activity detail cannot exceed 500 characters.");
        }
        return normalized;
    }

    private static WorkItemCollaborationResponse ToResponse(
        WorkItemCollaborationDocument collaboration,
        string userId) => new(
        collaboration.WorkItemId,
        collaboration.WatcherUserIds.Count,
        collaboration.VoterUserIds.Count,
        collaboration.WatcherUserIds.Contains(userId, StringComparer.Ordinal),
        collaboration.VoterUserIds.Contains(userId, StringComparer.Ordinal),
        collaboration.Version);

    private static string StableId(params string[] values)
    {
        var data = Encoding.UTF8.GetBytes(string.Join('\u001f', values));
        return Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();
    }

    private sealed record AuthorizedWorkItem(WorkItemDocument Item, string OrganizationId);
}
