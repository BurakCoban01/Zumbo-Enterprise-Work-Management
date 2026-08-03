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

    private async Task TryDeleteIndexAsync(string indexName, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await SendAsync(
                new HttpRequestMessage(HttpMethod.Delete, $"{BaseUrl}/{indexName}"),
                allowNotFound: true,
                cancellationToken);
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            // The inactive revision can be removed by the next maintenance run.
        }
    }
}
