using System.Globalization;
using System.Text.Json;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

public sealed record DashboardWidgetSourceResponse(
    string ProjectId,
    JsonElement Data,
    IReadOnlyCollection<DashboardTableColumn> Columns,
    IReadOnlyCollection<IReadOnlyDictionary<string, string?>> Rows,
    DateTimeOffset GeneratedAt,
    long SourceVersion,
    bool Stale);
