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
#pragma warning disable CS0169 // Preserved compatibility fields; active state belongs to OpenSearchTransport.
    private int consecutiveFailures;
    private DateTimeOffset circuitOpenUntil;
#pragma warning restore CS0169
    private readonly OpenSearchTransport transport;
    private readonly OpenSearchQueryClient queryClient;
    private readonly OpenSearchIndexManager indexManager;
    private readonly OpenSearchBulkWriter bulkWriter;

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
        transport = new OpenSearchTransport(httpClient, this.options, resiliencePolicy);
        indexManager = new OpenSearchIndexManager(transport, this.options, rebuildGate);
        bulkWriter = new OpenSearchBulkWriter(transport, this.options, indexManager, rebuildGate);
        var responseParser = new OpenSearchResponseParser();
        queryClient = new OpenSearchQueryClient(transport, this.options, responseParser);
    }

    private string AliasName => options.IndexName.Trim();
    private string VersionedIndexName => $"{AliasName}-v{options.MappingVersion}";
    private string BaseUrl => options.BaseUrl.TrimEnd('/');
}
