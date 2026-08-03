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
