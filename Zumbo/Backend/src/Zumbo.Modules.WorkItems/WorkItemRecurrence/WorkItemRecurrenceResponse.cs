using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

public sealed record WorkItemRecurrenceResponse(
    string Id,
    string ProjectId,
    string TemplateId,
    string Frequency,
    int Interval,
    DateTimeOffset StartAtUtc,
    DateTimeOffset? EndAtUtc,
    DateTimeOffset? NextRunAtUtc,
    int MaxOccurrences,
    int ScheduledOccurrences,
    long GeneratedOccurrences,
    bool Active,
    bool Archived,
    long Version) : IVersionedResource;
