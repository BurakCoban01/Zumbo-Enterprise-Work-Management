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

    public async Task IndexAsync(WorkItemSearchRecord record, CancellationToken cancellationToken = default)
    {
        ValidateScope(record.OrganizationId, record.ProjectId);
        using var request = JsonRequest(HttpMethod.Put, $"{BaseUrl}/{AliasName}/_doc/{Uri.EscapeDataString(record.Id)}", record);
        using var response = await SendAsync(request, cancellationToken: cancellationToken);
    }
}
