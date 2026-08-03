using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

public sealed record WorkItemRecurrenceOccurrenceResponse(
    string Id,
    DateTimeOffset ScheduledForUtc,
    string Status,
    string? CreatedWorkItemId,
    DateTimeOffset? GeneratedAt,
    long Version) : IVersionedResource;
