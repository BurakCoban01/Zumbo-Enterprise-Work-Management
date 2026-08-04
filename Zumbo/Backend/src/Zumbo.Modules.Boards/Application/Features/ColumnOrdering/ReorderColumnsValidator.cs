using Zumbo.SharedKernel;

namespace Zumbo.Modules.Boards.Application.Features.ColumnOrdering;

public static class ReorderColumnsValidator
{
    public static void Validate(ReorderColumnsRequest request)
    {
        if (request.ColumnIds is null || request.ColumnIds.Distinct().Count() != request.ColumnIds.Count)
        {
            throw new ValidationException("Column order must include each column exactly once.");
        }
    }
}
