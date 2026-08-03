using System.Globalization;
using System.Text.Json;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

public sealed record DashboardRenderResponse(
    DashboardResponse Dashboard,
    IReadOnlyCollection<DashboardRenderedWidgetResponse> Widgets,
    DateTimeOffset? GeneratedAt,
    IReadOnlyCollection<long> SourceVersions,
    bool Stale,
    bool Partial,
    DateTimeOffset RenderedAt);
