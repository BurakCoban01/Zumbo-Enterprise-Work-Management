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
    private static bool IsTransient(HttpStatusCode statusCode) =>
        OpenSearchTransport.IsTransient(statusCode);

    private void RegisterFailure()
    {
        transport.RegisterFailure();
    }

    private void ResetCircuit()
    {
        transport.ResetCircuit();
    }

    private void ThrowIfCircuitOpen()
    {
        transport.ThrowIfCircuitOpen();
    }
}
