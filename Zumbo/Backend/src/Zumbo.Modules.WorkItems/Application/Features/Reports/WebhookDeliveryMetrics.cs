using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Messaging;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

public sealed record WebhookDeliveryMetrics(
    long Pending,
    long Processing,
    long Delivered,
    long DeadLetter,
    DateTimeOffset? OldestPendingAtUtc,
    DateTimeOffset CapturedAtUtc);
