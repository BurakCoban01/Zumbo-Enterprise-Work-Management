using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Boards;

public sealed partial class BoardService{

    private static string NormalizeCategory(string category)
    {
        var normalized = string.IsNullOrWhiteSpace(category) ? "Custom" : category.Trim();
        var known = new[] { "Todo", "InProgress", "Review", "Test", "Done", "Custom" };
        return known.SingleOrDefault(x => x.Equals(normalized, StringComparison.OrdinalIgnoreCase)) ?? normalized;
    }
}
