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

internal static class RealtimeTelemetry
{
    internal static readonly ActivitySource ActivitySource = new("Zumbo.Realtime", "1.0.0");
    private static readonly Meter Meter = new("Zumbo.Realtime", "1.0.0");
    internal static readonly UpDownCounter<long> ActiveConnections = Meter.CreateUpDownCounter<long>("zumbo.realtime.active_connections");
    internal static readonly Counter<long> Published = Meter.CreateCounter<long>("zumbo.realtime.published");
    internal static readonly Counter<long> PublishFailures = Meter.CreateCounter<long>("zumbo.realtime.publish_failures");
    internal static readonly Histogram<double> PublishDuration = Meter.CreateHistogram<double>("zumbo.realtime.publish_duration", "ms");
}
