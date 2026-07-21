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

public sealed class WorkItemRealtimeOptions
{
    public string Backplane { get; init; } = "InMemory";
    public int MaximumProjectSubscriptionsPerConnection { get; init; } = 8;
    public int MaximumPayloadBytes { get; init; } = 16 * 1024;
    public int ApplicationMaxBufferBytes { get; init; } = 32 * 1024;
    public int TransportMaxBufferBytes { get; init; } = 64 * 1024;
    public int StatefulReconnectBufferBytes { get; init; } = 64 * 1024;
    public int SendTimeoutSeconds { get; init; } = 10;
    public int ClientTimeoutSeconds { get; init; } = 30;
    public int KeepAliveSeconds { get; init; } = 15;

    public void Validate()
    {
        if (MaximumProjectSubscriptionsPerConnection is < 1 or > 32)
            throw new InvalidOperationException("Realtime project subscription limit must be between 1 and 32.");
        if (MaximumPayloadBytes is < 2048 or > 65_536)
            throw new InvalidOperationException("Realtime payload limit must be between 2048 and 65536 bytes.");
        if (ApplicationMaxBufferBytes is < 4096 or > 262_144
            || TransportMaxBufferBytes < ApplicationMaxBufferBytes
            || TransportMaxBufferBytes > 1_048_576
            || StatefulReconnectBufferBytes is < 4096 or > 1_048_576)
        {
            throw new InvalidOperationException("Realtime connection buffers are invalid or unbounded.");
        }
        if (SendTimeoutSeconds is < 1 or > 30
            || KeepAliveSeconds is < 5 or > 30
            || ClientTimeoutSeconds < KeepAliveSeconds * 2
            || ClientTimeoutSeconds > 120)
        {
            throw new InvalidOperationException("Realtime timeout and keep-alive settings are invalid.");
        }
    }
}

public sealed record WorkItemRealtimeSubscription(
    string ProjectId,
    int SchemaVersion,
    int ActiveProjectSubscriptions);

internal static class RealtimeTelemetry
{
    internal static readonly ActivitySource ActivitySource = new("Zumbo.Realtime", "1.0.0");
    private static readonly Meter Meter = new("Zumbo.Realtime", "1.0.0");
    internal static readonly UpDownCounter<long> ActiveConnections = Meter.CreateUpDownCounter<long>("zumbo.realtime.active_connections");
    internal static readonly Counter<long> Published = Meter.CreateCounter<long>("zumbo.realtime.published");
    internal static readonly Counter<long> PublishFailures = Meter.CreateCounter<long>("zumbo.realtime.publish_failures");
    internal static readonly Histogram<double> PublishDuration = Meter.CreateHistogram<double>("zumbo.realtime.publish_duration", "ms");
}

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

public sealed class SignalRWorkItemRealtimePublisher(
    IHubContext<WorkItemHub> hubContext,
    IOptions<WorkItemRealtimeOptions> configuredOptions) : IWorkItemRealtimePublisher
{
    private readonly WorkItemRealtimeOptions options = Validate(configuredOptions.Value);

    public async Task PublishAsync(WorkItemRealtimeChange change, CancellationToken ct)
    {
        using var activity = RealtimeTelemetry.ActivitySource.StartActivity("signalr.publish", ActivityKind.Producer);
        activity?.SetTag("messaging.system", "signalr");
        activity?.SetTag("messaging.message.type", change.EventType);
        activity?.SetTag("zumbo.correlation_id", change.CorrelationId);
        var started = Stopwatch.GetTimestamp();
        if (change.SchemaVersion != WorkItemRealtimeProtocol.CurrentSchemaVersion
            || change.ResourceVersion < 0
            || string.IsNullOrWhiteSpace(change.ProjectId))
        {
            throw new InvalidOperationException("Realtime event protocol metadata is invalid.");
        }

        if (JsonSerializer.SerializeToUtf8Bytes(change).Length > options.MaximumPayloadBytes)
            throw new InvalidOperationException("Realtime event exceeds the configured payload limit.");

        try
        {
            await hubContext.Clients
                .Group(WorkItemHub.ProjectGroup(change.ProjectId))
                .SendAsync("workItemChanged", change, ct)
                .WaitAsync(TimeSpan.FromSeconds(options.SendTimeoutSeconds), ct);
            RealtimeTelemetry.Published.Add(1, new KeyValuePair<string, object?>("event.type", change.EventType));
            activity?.SetStatus(ActivityStatusCode.Ok);
        }
        catch (Exception exception)
        {
            RealtimeTelemetry.PublishFailures.Add(1, new KeyValuePair<string, object?>("exception.type", exception.GetType().Name));
            activity?.SetStatus(ActivityStatusCode.Error, exception.GetType().Name);
            throw;
        }
        finally
        {
            RealtimeTelemetry.PublishDuration.Record(Stopwatch.GetElapsedTime(started).TotalMilliseconds);
        }
    }

    private static WorkItemRealtimeOptions Validate(WorkItemRealtimeOptions value)
    {
        value.Validate();
        return value;
    }
}
