using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Messaging;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

public sealed record WebhookSubscriptionResponse(
    string Id,
    string Name,
    string TargetUrl,
    IReadOnlyCollection<string> EventScopes,
    bool IsActive,
    string SecretFingerprint,
    int SecretVersion,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    long Version);
