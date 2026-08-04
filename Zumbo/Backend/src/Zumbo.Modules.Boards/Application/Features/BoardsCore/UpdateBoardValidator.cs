using Zumbo.SharedKernel;

namespace Zumbo.Modules.Boards.Application.Features.BoardsCore;

public static class UpdateBoardValidator
{
    public static string NormalizeName(string name)
    {
        var normalized = name?.Trim() ?? string.Empty;
        if (normalized.Length is < 2 or > 100)
        {
            throw new ValidationException("Board name must contain 2-100 characters.");
        }

        return normalized;
    }

    public static string NormalizeType(string type)
    {
        if (string.IsNullOrWhiteSpace(type) || string.Equals(type, "Kanban", StringComparison.OrdinalIgnoreCase))
        {
            return "Kanban";
        }

        if (string.Equals(type, "Scrum", StringComparison.OrdinalIgnoreCase))
        {
            return "Scrum";
        }

        throw new ValidationException("Board type must be Kanban or Scrum.");
    }
}
