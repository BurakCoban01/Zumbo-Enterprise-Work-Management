using System.Text.RegularExpressions;
using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Projects;

public sealed partial class KnowledgeService{

    [GeneratedRegex(
        @"!?\[[^\]\r\n]{0,500}\]\(\s*(?<target>[^\s\)]+)",
        RegexOptions.CultureInvariant)]
    private static partial Regex MarkdownLinkPattern();
}
