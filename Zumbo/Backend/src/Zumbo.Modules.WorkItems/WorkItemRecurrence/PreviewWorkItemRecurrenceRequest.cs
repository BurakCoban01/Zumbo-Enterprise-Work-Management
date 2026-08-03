using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

public sealed record PreviewWorkItemRecurrenceRequest(
    string ProjectId,
    string TemplateId,
    string Frequency,
    int Interval,
    DateTimeOffset StartAtUtc,
    DateTimeOffset? EndAtUtc,
    int MaxOccurrences,
    int PreviewCount = 5);
