using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

public sealed record DashboardFilterRequest(
    int RangeDays = 30,
    int DueRiskDays = 30,
    string? AssigneeUserId = null,
    string? TeamId = null,
    IReadOnlyCollection<string>? Statuses = null);

public sealed record DashboardWidgetRequest(
    string Id,
    string Type,
    string Title,
    int Column,
    int Row,
    int Width,
    int Height,
    string? ProjectId = null,
    DashboardFilterRequest? Filter = null);

public sealed record SaveDashboardRequest(
    string Name,
    string? Description,
    string Scope,
    IReadOnlyCollection<string> ProjectIds,
    IReadOnlyCollection<DashboardWidgetRequest> Widgets,
    DashboardFilterRequest? Filter = null);

public sealed record ShareDashboardRequest(IReadOnlyCollection<string> ViewerUserIds);

public sealed record DashboardWidgetResponse(
    string Id,
    string Type,
    string Title,
    int Column,
    int Row,
    int Width,
    int Height,
    string? ProjectId,
    DashboardFilterRequest? Filter);

public sealed record DashboardResponse(
    string Id,
    string OwnerUserId,
    string Name,
    string? Description,
    string Scope,
    IReadOnlyCollection<string> ProjectIds,
    IReadOnlyCollection<DashboardWidgetResponse> Widgets,
    DashboardFilterRequest Filter,
    IReadOnlyCollection<string> ViewerUserIds,
    bool CanEdit,
    bool Archived,
    DateTimeOffset UpdatedAt,
    long Version) : IVersionedResource;

public sealed record DashboardPageResponse(
    IReadOnlyCollection<DashboardResponse> Items,
    int Page,
    int PageSize,
    long Total);

public interface IDashboardViewerDirectory
{
    Task EnsureOrganizationUsersAsync(
        string organizationId,
        IReadOnlyCollection<string> userIds,
        CancellationToken ct);
}

public interface IDashboardAuditWriter
{
    Task WriteAsync(
        string action,
        string dashboardId,
        string? oldValue,
        string? newValue,
        string correlationId,
        CancellationToken ct);
}

public sealed class DashboardService(
    IDocumentRepository<DashboardDocument> dashboards,
    IProjectPermissionChecker projectPermissions,
    IDashboardViewerDirectory viewerDirectory,
    IDashboardAuditWriter audit,
    ICurrentUser currentUser,
    IClock clock,
    IExpectedVersionAccessor? expectedVersions = null)
{
    private const int MaximumProjects = 20;
    private const int MaximumWidgets = 12;
    private const int MaximumWidgetSources = 60;
    private readonly ExpectedVersionState expectedVersion = new(expectedVersions);

    public async Task<DashboardResponse> SaveAsync(
        string? dashboardId,
        SaveDashboardRequest request,
        string correlationId,
        CancellationToken ct)
    {
        var definition = Normalize(request);
        var actor = CurrentActor();
        await EnsureProjectsAsync(definition.Scope, definition.ProjectIds, actor.UserId, ct);
        var now = clock.UtcNow;

        DashboardDocument dashboard;
        if (string.IsNullOrWhiteSpace(dashboardId))
        {
            dashboard = new DashboardDocument
            {
                OrganizationId = actor.OrganizationId,
                OwnerUserId = actor.UserId,
                CreatedAt = now
            };
            Apply(dashboard, definition, now);
            dashboard = await dashboards.CreateAsync(dashboard, ct);
            await audit.WriteAsync(
                "DashboardCreated", dashboard.Id, null, dashboard.Name, correlationId, ct);
        }
        else
        {
            dashboard = await GetDocumentAsync(dashboardId, includeArchived: false, ct);
            EnsureOwner(dashboard, actor);
            var oldValue = dashboard.Name;
            Apply(dashboard, definition, now);
            await ReplaceAsync(dashboard, ct);
            await audit.WriteAsync(
                "DashboardUpdated", dashboard.Id, oldValue, dashboard.Name, correlationId, ct);
        }

        return ToResponse(dashboard, actor.UserId);
    }

    public async Task<DashboardPageResponse> ListAsync(
        bool includeArchived,
        int page,
        int pageSize,
        CancellationToken ct)
    {
        var actor = CurrentActor();
        var normalizedPage = Math.Max(page, 1);
        var normalizedPageSize = Math.Clamp(pageSize, 1, 100);
        var visible = new List<DashboardDocument>();
        string? cursor = null;
        do
        {
            var batch = await dashboards.ListByCursorAsync(
                item => item.OrganizationId == actor.OrganizationId
                    && (includeArchived || !item.Archived),
                cursor,
                100,
                ct);
            foreach (var item in batch.Items.Where(item =>
                         item.OwnerUserId == actor.UserId || item.ViewerUserIds.Contains(actor.UserId)))
            {
                if (await CanViewProjectsAsync(item.ProjectIds, actor.UserId, ct))
                    visible.Add(item);
            }
            cursor = batch.NextCursor;
        } while (cursor is not null);

        var ordered = visible
            .OrderByDescending(item => item.UpdatedAt)
            .ThenBy(item => item.Id, StringComparer.Ordinal)
            .ToList();
        return new DashboardPageResponse(
            ordered.Skip((normalizedPage - 1) * normalizedPageSize)
                .Take(normalizedPageSize)
                .Select(item => ToResponse(item, actor.UserId))
                .ToList(),
            normalizedPage,
            normalizedPageSize,
            ordered.Count);
    }

    public async Task<DashboardResponse> GetAsync(
        string dashboardId,
        bool includeArchived,
        CancellationToken ct)
    {
        var actor = CurrentActor();
        var dashboard = await GetDocumentAsync(dashboardId, includeArchived, ct);
        EnsureVisible(dashboard, actor);
        await EnsureProjectsAsync(DashboardScopes.Personal, dashboard.ProjectIds, actor.UserId, ct);
        return ToResponse(dashboard, actor.UserId);
    }

    public async Task<DashboardResponse> ShareAsync(
        string dashboardId,
        ShareDashboardRequest request,
        string correlationId,
        CancellationToken ct)
    {
        var actor = CurrentActor();
        var dashboard = await GetDocumentAsync(dashboardId, includeArchived: false, ct);
        EnsureOwner(dashboard, actor);
        var viewers = (request.ViewerUserIds
                ?? throw new ValidationException("Dashboard viewer list is required."))
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .Where(value => value != actor.UserId)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToList();
        if (viewers.Count > 50)
            throw new ValidationException("A dashboard cannot be shared with more than 50 users.");
        if (viewers.Any(value => value.Length > 128))
            throw new ValidationException("Dashboard viewer user ids cannot exceed 128 characters.");
        await viewerDirectory.EnsureOrganizationUsersAsync(dashboard.OrganizationId, viewers, ct);
        var oldValue = string.Join(",", dashboard.ViewerUserIds.Order(StringComparer.Ordinal));
        dashboard.ViewerUserIds = viewers;
        dashboard.UpdatedAt = clock.UtcNow;
        await ReplaceAsync(dashboard, ct);
        await audit.WriteAsync(
            "DashboardSharingChanged",
            dashboard.Id,
            oldValue,
            string.Join(",", viewers),
            correlationId,
            ct);
        return ToResponse(dashboard, actor.UserId);
    }

    public async Task ArchiveAsync(
        string dashboardId,
        string correlationId,
        CancellationToken ct)
    {
        var actor = CurrentActor();
        var dashboard = await GetDocumentAsync(dashboardId, includeArchived: false, ct);
        EnsureOwner(dashboard, actor);
        dashboard.Archived = true;
        dashboard.UpdatedAt = clock.UtcNow;
        await ReplaceAsync(dashboard, ct);
        await audit.WriteAsync(
            "DashboardArchived", dashboard.Id, "Active", "Archived", correlationId, ct);
    }

    private async Task EnsureProjectsAsync(
        string scope,
        IReadOnlyCollection<string> projectIds,
        string userId,
        CancellationToken ct)
    {
        var permission = scope == DashboardScopes.Personal
            ? PermissionCatalog.WorkItemView
            : PermissionCatalog.WorkItemUpdate;
        foreach (var projectId in projectIds)
        {
            _ = await projectPermissions.EnsureCanAsync(userId, projectId, permission, ct);
        }
    }

    private async Task<bool> CanViewProjectsAsync(
        IReadOnlyCollection<string> projectIds,
        string userId,
        CancellationToken ct)
    {
        try
        {
            await EnsureProjectsAsync(DashboardScopes.Personal, projectIds, userId, ct);
            return true;
        }
        catch (NotFoundException)
        {
            return false;
        }
        catch (ForbiddenException)
        {
            return false;
        }
    }

    private async Task<DashboardDocument> GetDocumentAsync(
        string dashboardId,
        bool includeArchived,
        CancellationToken ct)
    {
        var actor = CurrentActor();
        return await dashboards.SelectAsync(
            item => item.Id == dashboardId
                && item.OrganizationId == actor.OrganizationId
                && (includeArchived || !item.Archived),
            ct)
            ?? throw new NotFoundException("DASHBOARD_NOT_FOUND", "Dashboard was not found.");
    }

    private async Task ReplaceAsync(DashboardDocument dashboard, CancellationToken ct)
    {
        var result = await dashboards.ReplaceByVersionAsync(
            item => item.Id == dashboard.Id && item.OrganizationId == dashboard.OrganizationId,
            dashboard,
            expectedVersion.Consume(dashboard.Version),
            ct);
        if (!result.Found)
            throw new NotFoundException("DASHBOARD_NOT_FOUND", "Dashboard was not found.");
        dashboard.Version = result.Version!.Value;
    }

    private (string UserId, string OrganizationId) CurrentActor()
    {
        var userId = currentUser.UserId
            ?? throw new UnauthorizedException("Authenticated user is required.");
        var organizationId = currentUser.OrganizationId
            ?? throw new UnauthorizedException("Active organization is required.");
        return (userId, organizationId);
    }

    private static void EnsureVisible(
        DashboardDocument dashboard,
        (string UserId, string OrganizationId) actor)
    {
        if (dashboard.OwnerUserId != actor.UserId
            && !dashboard.ViewerUserIds.Contains(actor.UserId, StringComparer.Ordinal))
        {
            throw new NotFoundException("DASHBOARD_NOT_FOUND", "Dashboard was not found.");
        }
    }

    private static void EnsureOwner(
        DashboardDocument dashboard,
        (string UserId, string OrganizationId) actor)
    {
        EnsureVisible(dashboard, actor);
        if (dashboard.OwnerUserId != actor.UserId)
            throw new ForbiddenException("Only the dashboard owner can change this dashboard.");
    }

    private static SaveDashboardRequest Normalize(SaveDashboardRequest request)
    {
        var name = Required(request.Name, "Dashboard name", 120);
        var description = Optional(request.Description, 500);
        var scope = request.Scope?.Trim() switch
        {
            DashboardScopes.Personal => DashboardScopes.Personal,
            DashboardScopes.Project => DashboardScopes.Project,
            DashboardScopes.Portfolio => DashboardScopes.Portfolio,
            _ => throw new ValidationException("Dashboard scope must be Personal, Project or Portfolio.")
        };
        var projectIds = (request.ProjectIds
                ?? throw new ValidationException("Dashboard project list is required."))
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToList();
        if (projectIds.Count is < 1 or > MaximumProjects)
            throw new ValidationException($"A dashboard must contain between 1 and {MaximumProjects} projects.");
        if (scope == DashboardScopes.Project && projectIds.Count != 1)
            throw new ValidationException("A project dashboard must contain exactly one project.");
        if (scope == DashboardScopes.Portfolio && projectIds.Count < 2)
            throw new ValidationException("A portfolio dashboard must contain at least two projects.");
        var requestedWidgets = request.Widgets
            ?? throw new ValidationException("Dashboard widget list is required.");
        if (requestedWidgets.Count is < 1 or > MaximumWidgets)
            throw new ValidationException($"A dashboard must contain between 1 and {MaximumWidgets} widgets.");

        var widgetIds = new HashSet<string>(StringComparer.Ordinal);
        var widgets = requestedWidgets.Select(widget =>
        {
            var id = Required(widget.Id, "Widget id", 64);
            if (!widgetIds.Add(id))
                throw new ValidationException("Dashboard widget ids must be unique.");
            if (!DashboardWidgetTypes.Allowed.Contains(widget.Type))
                throw new ValidationException($"Dashboard widget type '{widget.Type}' is not supported.");
            if (widget.Column is < 1 or > 12 || widget.Width is < 1 or > 12
                || widget.Column + widget.Width - 1 > 12)
                throw new ValidationException("Dashboard widgets must fit within the 12-column layout.");
            if (widget.Row is < 1 or > 100 || widget.Height is < 1 or > 6)
                throw new ValidationException("Dashboard widget row or height is outside the supported bounds.");
            var projectId = Optional(widget.ProjectId, 128);
            if (projectId is not null && !projectIds.Contains(projectId, StringComparer.Ordinal))
                throw new ValidationException("Widget project must belong to the dashboard project scope.");
            return widget with
            {
                Id = id,
                Title = Required(widget.Title, "Widget title", 120),
                ProjectId = projectId,
                Filter = widget.Filter is null ? null : NormalizeFilter(widget.Filter)
            };
        }).ToList();
        for (var leftIndex = 0; leftIndex < widgets.Count; leftIndex++)
        {
            for (var rightIndex = leftIndex + 1; rightIndex < widgets.Count; rightIndex++)
            {
                if (Overlaps(widgets[leftIndex], widgets[rightIndex]))
                    throw new ValidationException("Dashboard widgets cannot overlap.");
            }
        }
        var sourceCount = widgets.Sum(widget => widget.ProjectId is null ? projectIds.Count : 1);
        if (sourceCount > MaximumWidgetSources)
        {
            throw new ValidationException(
                $"A dashboard cannot query more than {MaximumWidgetSources} widget sources.");
        }

        return request with
        {
            Name = name,
            Description = description,
            Scope = scope,
            ProjectIds = projectIds,
            Widgets = widgets,
            Filter = NormalizeFilter(request.Filter ?? new DashboardFilterRequest())
        };
    }

    private static DashboardFilterRequest NormalizeFilter(DashboardFilterRequest filter)
    {
        if (filter.RangeDays is < 1 or > 366)
            throw new ValidationException("Dashboard range must be between 1 and 366 days.");
        if (filter.DueRiskDays is < 1 or > 90)
            throw new ValidationException("Dashboard due-risk range must be between 1 and 90 days.");
        var statuses = (filter.Statuses ?? [])
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (statuses.Count > 20 || statuses.Any(value => value.Length > 64))
            throw new ValidationException("Dashboard status filters are outside the supported bounds.");
        return filter with
        {
            AssigneeUserId = Optional(filter.AssigneeUserId, 128),
            TeamId = Optional(filter.TeamId, 128),
            Statuses = statuses
        };
    }

    private static bool Overlaps(DashboardWidgetRequest left, DashboardWidgetRequest right) =>
        left.Column < right.Column + right.Width
        && left.Column + left.Width > right.Column
        && left.Row < right.Row + right.Height
        && left.Row + left.Height > right.Row;

    private static void Apply(
        DashboardDocument dashboard,
        SaveDashboardRequest definition,
        DateTimeOffset now)
    {
        dashboard.Name = definition.Name;
        dashboard.Description = definition.Description;
        dashboard.Scope = definition.Scope;
        dashboard.ProjectIds = definition.ProjectIds.ToList();
        dashboard.Filter = ToDocument(definition.Filter!);
        dashboard.Widgets = definition.Widgets.Select(widget => new DashboardWidgetDocument
        {
            Id = widget.Id,
            Type = widget.Type,
            Title = widget.Title,
            Column = widget.Column,
            Row = widget.Row,
            Width = widget.Width,
            Height = widget.Height,
            ProjectId = widget.ProjectId,
            Filter = widget.Filter is null ? null : ToDocument(widget.Filter)
        }).ToList();
        dashboard.UpdatedAt = now;
    }

    private static DashboardResponse ToResponse(DashboardDocument item, string userId) => new(
        item.Id,
        item.OwnerUserId,
        item.Name,
        item.Description,
        item.Scope,
        item.ProjectIds,
        item.Widgets.Select(widget => new DashboardWidgetResponse(
            widget.Id,
            widget.Type,
            widget.Title,
            widget.Column,
            widget.Row,
            widget.Width,
            widget.Height,
            widget.ProjectId,
            widget.Filter is null ? null : ToRequest(widget.Filter))).ToList(),
        ToRequest(item.Filter),
        item.ViewerUserIds,
        item.OwnerUserId == userId,
        item.Archived,
        item.UpdatedAt,
        item.Version);

    private static DashboardFilterDocument ToDocument(DashboardFilterRequest filter) => new()
    {
        RangeDays = filter.RangeDays,
        DueRiskDays = filter.DueRiskDays,
        AssigneeUserId = filter.AssigneeUserId,
        TeamId = filter.TeamId,
        Statuses = filter.Statuses?.ToList() ?? []
    };

    private static DashboardFilterRequest ToRequest(DashboardFilterDocument filter) => new(
        filter.RangeDays,
        filter.DueRiskDays,
        filter.AssigneeUserId,
        filter.TeamId,
        filter.Statuses);

    private static string Required(string? value, string label, int maximum)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ValidationException($"{label} is required.");
        var normalized = value.Trim();
        if (normalized.Length > maximum)
            throw new ValidationException($"{label} cannot exceed {maximum} characters.");
        return normalized;
    }

    private static string? Optional(string? value, int maximum)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        var normalized = value.Trim();
        if (normalized.Length > maximum)
            throw new ValidationException($"Value cannot exceed {maximum} characters.");
        return normalized;
    }
}
