using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using System.Linq.Expressions;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Audit;

public sealed class AuditOptions
{
    public int RetentionDays { get; init; } = 365;
    public int ExportMaxRecords { get; init; } = 10_000;
    public int RetentionBatchSize { get; init; } = 200;
    public int IntegrityMaxRecords { get; init; } = 100_000;
    public bool HashChainEnabled { get; init; }
    public string IntegrityKey { get; init; } = string.Empty;
}
