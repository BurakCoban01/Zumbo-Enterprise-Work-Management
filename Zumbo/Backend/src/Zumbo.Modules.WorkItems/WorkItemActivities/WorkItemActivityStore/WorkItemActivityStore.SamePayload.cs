using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Linq.Expressions;
using System.Runtime.CompilerServices;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

public sealed partial class WorkItemActivityStore{

    private static bool SamePayload<TDocument>(TDocument stored, TDocument expected)
        where TDocument : class, IVersionedDocument
    {
        var storedNode = JsonSerializer.SerializeToNode(stored)?.AsObject();
        var expectedNode = JsonSerializer.SerializeToNode(expected)?.AsObject();
        storedNode?.Remove(nameof(IVersionedDocument.Version));
        expectedNode?.Remove(nameof(IVersionedDocument.Version));
        return JsonNode.DeepEquals(storedNode, expectedNode);
    }
}
