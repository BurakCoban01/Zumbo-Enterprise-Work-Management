using System.Globalization;
using System.Text.Json;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

public sealed record DashboardRenderedWidgetResponse(
    string Id,
    string Type,
    string Title,
    string Status,
    string? ErrorCode,
    IReadOnlyCollection<DashboardWidgetSourceResponse> Sources);
