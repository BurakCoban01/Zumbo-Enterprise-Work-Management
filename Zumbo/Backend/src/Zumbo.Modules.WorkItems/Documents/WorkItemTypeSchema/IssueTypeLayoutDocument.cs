using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

public sealed class IssueTypeLayoutDocument
{
    public string IssueTypeKey { get; set; } = string.Empty;
    public List<string> FieldKeys { get; set; } = [];
}
