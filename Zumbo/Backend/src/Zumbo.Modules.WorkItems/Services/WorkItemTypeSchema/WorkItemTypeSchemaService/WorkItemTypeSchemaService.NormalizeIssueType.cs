using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

public sealed partial class WorkItemTypeSchemaService{

    private static IssueTypeDefinitionDocument NormalizeIssueType(IssueTypeDefinitionRequest request)
    {
        var key = NormalizeKey(request.Key);
        var name = request.Name?.Trim() ?? string.Empty;
        if (!KeyPattern().IsMatch(key) || name.Length is < 1 or > 100)
        {
            throw new ValidationException("Issue type keys and names are invalid.");
        }

        var hierarchy = Canonical(IssueTypeHierarchyLevels.All, request.HierarchyLevel, "issue type hierarchy level");
        return new IssueTypeDefinitionDocument
        {
            Key = key,
            Name = name,
            Description = request.Description?.Trim() ?? string.Empty,
            HierarchyLevel = hierarchy,
            Active = request.Active,
            Position = Math.Clamp(request.Position, 0, 10_000)
        };
    }
}
