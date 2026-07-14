using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Zumbo.Modules.WorkItems;

[Authorize]
public sealed class WorkItemHub(IProjectPermissionChecker permissionChecker) : Hub
{
    public async Task SubscribeProject(string projectId)
    {
        if (string.IsNullOrWhiteSpace(projectId) || projectId.Length > 128)
        {
            throw new HubException("A valid project id is required.");
        }

        var userId = Context.UserIdentifier;
        if (string.IsNullOrWhiteSpace(userId))
        {
            throw new HubException("An authenticated user is required.");
        }

        var normalizedProjectId = projectId.Trim();
        await permissionChecker.EnsureCanAsync(
            userId,
            normalizedProjectId,
            "WorkItemView",
            Context.ConnectionAborted);
        await Groups.AddToGroupAsync(Context.ConnectionId, ProjectGroup(normalizedProjectId));
    }

    public Task UnsubscribeProject(string projectId) =>
        Groups.RemoveFromGroupAsync(Context.ConnectionId, ProjectGroup(projectId.Trim()));

    internal static string ProjectGroup(string projectId) => "project:" + projectId;
}

public sealed class SignalRWorkItemRealtimePublisher(IHubContext<WorkItemHub> hubContext)
    : IWorkItemRealtimePublisher
{
    public Task PublishAsync(WorkItemRealtimeChange change, CancellationToken ct) =>
        hubContext.Clients
            .Group(WorkItemHub.ProjectGroup(change.ProjectId))
            .SendAsync("workItemChanged", change, ct);
}
