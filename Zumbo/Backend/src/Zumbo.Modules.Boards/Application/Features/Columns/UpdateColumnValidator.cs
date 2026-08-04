namespace Zumbo.Modules.Boards.Application.Features.Columns;

public static class UpdateColumnValidator
{
    public static string NormalizeName(string name) => AddColumnValidator.NormalizeName(name);

    public static string NormalizeCategory(string category) => AddColumnValidator.NormalizeCategory(category);

    public static void ValidateWipLimit(int? wipLimit) => AddColumnValidator.ValidateWipLimit(wipLimit);
}
