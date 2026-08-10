using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Runtime;
using Zumbo.BuildingBlocks.Application.Search;

namespace Zumbo.BuildingBlocks.Infrastructure.Search;

public sealed class OpenSearchOptions
{
    public string BaseUrl { get; init; } = string.Empty;
    public string IndexName { get; init; } = "zumbo-work-items";
    public int MappingVersion { get; init; } = 1;
    public int NumberOfShards { get; init; } = 1;
    public int NumberOfReplicas { get; init; } = 1;
    public string? Username { get; init; }
    public string? Password { get; init; }
    public bool AllowInsecureHttp { get; init; }
    public int RequestTimeoutSeconds { get; init; } = 5;
    public int CircuitFailureThreshold { get; init; } = 3;
    public int CircuitBreakSeconds { get; init; } = 30;
    public int MaxReindexItems { get; init; } = 10_000;
}
