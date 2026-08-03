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

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        ValidateConfiguration(options);
        await EnsureIndexAsync(VersionedIndexName, cancellationToken);

        using var aliasResponse = await SendAsync(
            new HttpRequestMessage(HttpMethod.Head, $"{BaseUrl}/_alias/{AliasName}"),
            allowNotFound: true,
            cancellationToken);
        if (aliasResponse.StatusCode == HttpStatusCode.NotFound)
        {
            using var concreteIndexResponse = await SendAsync(
                new HttpRequestMessage(HttpMethod.Head, $"{BaseUrl}/{AliasName}"),
                allowNotFound: true,
                cancellationToken);
            if (concreteIndexResponse.IsSuccessStatusCode)
            {
                await MigrateLegacyConcreteIndexAsync(cancellationToken);
                return;
            }

            await ChangeAliasAsync([], VersionedIndexName, cancellationToken);
        }
    }
}
