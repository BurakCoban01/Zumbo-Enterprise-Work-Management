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

    private void ThrowIfCircuitOpen()
    {
        lock (circuitGate)
        {
            if (circuitOpenUntil > DateTimeOffset.UtcNow)
                throw new WorkItemSearchUnavailableException("OpenSearch circuit is open.");
            if (circuitOpenUntil != default)
            {
                circuitOpenUntil = default;
                consecutiveFailures = 0;
            }
        }
    }
}
