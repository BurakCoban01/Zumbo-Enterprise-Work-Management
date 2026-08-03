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

    public async Task DeleteAsync(string id, CancellationToken cancellationToken = default)
    {
        using var response = await SendAsync(
            new HttpRequestMessage(HttpMethod.Delete, $"{BaseUrl}/{AliasName}/_doc/{Uri.EscapeDataString(id)}"),
            allowNotFound: true,
            cancellationToken);
    }
}
