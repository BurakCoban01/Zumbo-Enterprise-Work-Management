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

    private async Task<IReadOnlyList<string>> GetAliasIndexesAsync(CancellationToken cancellationToken)
    {
        using var response = await SendAsync(
            new HttpRequestMessage(HttpMethod.Get, $"{BaseUrl}/_alias/{AliasName}"),
            allowNotFound: true,
            cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound) return [];
        using var payload = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
        return payload.RootElement.EnumerateObject().Select(x => x.Name).Order(StringComparer.Ordinal).ToList();
    }
}
