using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.Modules.WorkItems;

[Authorize]
public sealed class WorkItemHub(
    IProjectPermissionChecker permissionChecker,
    IOptions<WorkItemRealtimeOptions> configuredOptions,
    IConfiguration configuration) : Hub
{
    private const string SubscriptionStateKey = "work-item-project-subscriptions";
    private const string ConnectionCountedKey = "realtime-connection-counted";
    private readonly WorkItemRealtimeOptions options = Validate(configuredOptions.Value);

    public string GetInstanceId() =>
        configuration["Runtime:InstanceId"] ?? Environment.MachineName;

    public override async Task OnConnectedAsync()
    {
        Context.Items[ConnectionCountedKey] = true;
        RealtimeTelemetry.ActiveConnections.Add(1);
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        if (Context.Items.Remove(ConnectionCountedKey)) RealtimeTelemetry.ActiveConnections.Add(-1);
        await base.OnDisconnectedAsync(exception);
    }

    public async Task<WorkItemRealtimeSubscription> SubscribeProject(string projectId)
    {
        using var activity = RealtimeTelemetry.ActivitySource.StartActivity("signalr.subscribe", ActivityKind.Server);
        var normalizedProjectId = ValidateProjectId(projectId);
        var userId = Context.UserIdentifier;
        if (string.IsNullOrWhiteSpace(userId))
            throw new HubException("An authenticated user is required.");

        await permissionChecker.EnsureCanAsync(
            userId,
            normalizedProjectId,
            PermissionCatalog.WorkItemView,
            Context.ConnectionAborted);
        var subscriptions = Subscriptions();
        if (!subscriptions.Contains(normalizedProjectId)
            && subscriptions.Count >= options.MaximumProjectSubscriptionsPerConnection)
        {
            throw new HubException("The realtime project subscription limit was reached.");
        }

        if (subscriptions.Add(normalizedProjectId))
            await Groups.AddToGroupAsync(Context.ConnectionId, ProjectGroup(normalizedProjectId));
        activity?.SetTag("zumbo.subscription_count", subscriptions.Count);
        activity?.SetStatus(ActivityStatusCode.Ok);
        return new WorkItemRealtimeSubscription(
            normalizedProjectId,
            WorkItemRealtimeProtocol.CurrentSchemaVersion,
            subscriptions.Count);
    }

    public async Task UnsubscribeProject(string projectId)
    {
        var normalizedProjectId = ValidateProjectId(projectId);
        if (Subscriptions().Remove(normalizedProjectId))
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, ProjectGroup(normalizedProjectId));
    }

    internal static string ProjectGroup(string projectId)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(projectId));
        return "project:" + Convert.ToHexString(hash).ToLowerInvariant();
    }

    private HashSet<string> Subscriptions()
    {
        if (Context.Items.TryGetValue(SubscriptionStateKey, out var value)
            && value is HashSet<string> subscriptions)
        {
            return subscriptions;
        }

        subscriptions = new HashSet<string>(StringComparer.Ordinal);
        Context.Items[SubscriptionStateKey] = subscriptions;
        return subscriptions;
    }

    private static string ValidateProjectId(string projectId)
    {
        if (string.IsNullOrWhiteSpace(projectId) || projectId.Trim().Length > 128)
            throw new HubException("A valid project id is required.");
        return projectId.Trim();
    }

    private static WorkItemRealtimeOptions Validate(WorkItemRealtimeOptions value)
    {
        value.Validate();
        return value;
    }
}
