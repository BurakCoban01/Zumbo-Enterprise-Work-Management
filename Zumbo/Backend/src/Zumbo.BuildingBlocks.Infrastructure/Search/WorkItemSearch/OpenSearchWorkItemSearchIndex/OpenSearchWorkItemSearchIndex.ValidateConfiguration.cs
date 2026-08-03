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

public sealed partial class OpenSearchWorkItemSearchIndex {

    public static void ValidateConfiguration(OpenSearchOptions options)
    {
        if (!Uri.TryCreate(options.BaseUrl, UriKind.Absolute, out var baseUri)
            || (baseUri.Scheme != Uri.UriSchemeHttp && baseUri.Scheme != Uri.UriSchemeHttps))
            throw new InvalidOperationException("OpenSearch base URL must be an absolute HTTP or HTTPS URL.");
        if (baseUri.Scheme == Uri.UriSchemeHttp && !options.AllowInsecureHttp)
            throw new InvalidOperationException("OpenSearch HTTP requires AllowInsecureHttp=true; use HTTPS otherwise.");
        if (string.IsNullOrWhiteSpace(options.IndexName)
            || options.IndexName.Any(char.IsUpper)
            || options.IndexName.IndexOfAny([' ', '/', '\\', '*', '?', '#', ',']) >= 0)
            throw new InvalidOperationException("OpenSearch index name is invalid.");
        if (options.MappingVersion < 1 || options.NumberOfShards < 1 || options.NumberOfReplicas < 0)
            throw new InvalidOperationException("OpenSearch mapping, shard and replica settings are invalid.");
        if (string.IsNullOrWhiteSpace(options.Username) != string.IsNullOrWhiteSpace(options.Password))
            throw new InvalidOperationException("OpenSearch username and password must be configured together.");
        if (options.RequestTimeoutSeconds is < 1 or > 120
            || options.CircuitFailureThreshold is < 1 or > 20
            || options.CircuitBreakSeconds is < 1 or > 300
            || options.MaxReindexItems is < 1 or > 100_000)
            throw new InvalidOperationException("OpenSearch timeout, circuit and reindex limits are outside supported bounds.");
    }
}
