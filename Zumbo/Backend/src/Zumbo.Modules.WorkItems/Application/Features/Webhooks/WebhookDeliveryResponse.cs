using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Messaging;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

public sealed record WebhookDeliveryResponse(
    string Id,
    string SubscriptionId,
    string EventScope,
    string PayloadSha256,
    string Status,
    int Attempts,
    DateTimeOffset NextAttemptAtUtc,
    string? LastErrorCode,
    DateTimeOffset? DeliveredAtUtc,
    DateTimeOffset? DeadLetteredAtUtc,
    DateTimeOffset CreatedAtUtc,
    long Version);
