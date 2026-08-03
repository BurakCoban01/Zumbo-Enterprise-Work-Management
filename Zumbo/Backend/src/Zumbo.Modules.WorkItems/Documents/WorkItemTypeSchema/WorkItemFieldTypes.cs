using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

public static class WorkItemFieldTypes
{
    public const string Text = "Text";
    public const string Number = "Number";
    public const string Boolean = "Boolean";
    public const string Date = "Date";
    public const string Select = "Select";

    public static IReadOnlySet<string> All { get; } = new HashSet<string>(
        [Text, Number, Boolean, Date, Select],
        StringComparer.Ordinal);
}
