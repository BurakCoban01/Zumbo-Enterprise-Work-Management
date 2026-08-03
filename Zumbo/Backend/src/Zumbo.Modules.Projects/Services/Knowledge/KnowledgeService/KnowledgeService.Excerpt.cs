using System.Text.RegularExpressions;
using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Projects;

public sealed partial class KnowledgeService{

    private static string Excerpt(string value)
    {
        var compact = WhitespacePattern().Replace(value, " ").Trim();
        return compact.Length <= 220 ? compact : compact[..217] + "...";
    }
}
