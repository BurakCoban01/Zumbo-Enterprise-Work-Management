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

    private async Task ChangeAliasAsync(
        IReadOnlyCollection<string> oldIndexes,
        string newIndex,
        CancellationToken cancellationToken)
    {
        var actions = oldIndexes
            .Where(index => !index.Equals(newIndex, StringComparison.Ordinal))
            .Select(index => (object)new { remove = new { index, alias = AliasName } })
            .Append(new { add = new { index = newIndex, alias = AliasName, is_write_index = true } })
            .ToList();
        using var request = JsonRequest(HttpMethod.Post, $"{BaseUrl}/_aliases", new { actions });
        using var response = await SendAsync(request, cancellationToken: cancellationToken);
    }
}
