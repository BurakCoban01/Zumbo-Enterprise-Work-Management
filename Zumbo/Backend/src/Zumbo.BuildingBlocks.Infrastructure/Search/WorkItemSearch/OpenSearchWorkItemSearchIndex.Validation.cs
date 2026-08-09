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

public sealed partial class OpenSearchWorkItemSearchIndex
{
    public static void ValidateConfiguration(OpenSearchOptions options)
        => OpenSearchValidation.ValidateConfiguration(options);

    private static void ValidateScope(string organizationId, string projectId)
        => OpenSearchValidation.ValidateScope(organizationId, projectId);
}
