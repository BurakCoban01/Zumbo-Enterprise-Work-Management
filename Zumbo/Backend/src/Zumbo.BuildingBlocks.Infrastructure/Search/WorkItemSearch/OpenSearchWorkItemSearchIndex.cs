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

public sealed partial class OpenSearchWorkItemSearchIndex : IWorkItemSearchIndex
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly HttpClient httpClient;
    private readonly OpenSearchOptions options;
    private readonly IExternalDependencyPolicy? resiliencePolicy;
    private readonly object circuitGate = new();
    private readonly SemaphoreSlim rebuildGate = new(1, 1);
    private int consecutiveFailures;
    private DateTimeOffset circuitOpenUntil;

    public OpenSearchWorkItemSearchIndex(HttpClient httpClient, IOptions<OpenSearchOptions> options)
        : this(httpClient, options, null)
    {
    }

    public OpenSearchWorkItemSearchIndex(
        HttpClient httpClient,
        IOptions<OpenSearchOptions> options,
        IExternalDependencyPolicyProvider? policyProvider)
    {
        this.httpClient = httpClient;
        this.options = options.Value;
        resiliencePolicy = policyProvider?.Get(ExternalDependencyNames.OpenSearch);
    }

    private string AliasName => options.IndexName.Trim();
    private string VersionedIndexName => $"{AliasName}-v{options.MappingVersion}";
    private string BaseUrl => options.BaseUrl.TrimEnd('/');
}
