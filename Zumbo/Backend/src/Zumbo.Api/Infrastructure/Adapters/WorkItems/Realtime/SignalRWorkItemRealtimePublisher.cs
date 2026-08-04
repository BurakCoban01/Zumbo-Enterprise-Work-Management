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
