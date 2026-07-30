using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Projects;

public sealed record SavePortfolioRequest(
    string Name,
    string? Description,
    IReadOnlyCollection<string> ViewerUserIds);

public sealed record SaveInitiativeRequest(
    string Name,
    string? Summary,
    string? ParentInitiativeId,
    string OwnerUserId,
    string Status,
    string Health,
    int? Confidence,
    DateTimeOffset? TargetAt,
    IReadOnlyCollection<string> ProjectIds,
    IReadOnlyCollection<PortfolioMilestoneLinkRequest> MilestoneLinks);

public sealed record PortfolioMilestoneLinkRequest(string ProjectId, string MilestoneId);

public sealed record AddInitiativeStatusUpdateRequest(
    string Status,
    string Health,
    int? Confidence,
    string Note);

public sealed record SavePortfolioDependencyRequest(
    string SourceProjectId,
    string TargetProjectId,
    string Description,
    string Status,
    DateTimeOffset? RequiredBy);

public sealed record InitiativeStatusUpdateResponse(
    string Id,
    string Status,
    string Health,
    int? Confidence,
    string Note,
    string AuthorUserId,
    DateTimeOffset CreatedAt);

public sealed record PortfolioMilestoneLinkResponse(string ProjectId, string MilestoneId);

public sealed record InitiativeResponse(
    string Id,
    string Name,
    string? Summary,
    string? ParentInitiativeId,
    string OwnerUserId,
    string Status,
    string Health,
    int? Confidence,
    DateTimeOffset? TargetAt,
    IReadOnlyCollection<string> ProjectIds,
    IReadOnlyCollection<PortfolioMilestoneLinkResponse> MilestoneLinks,
    IReadOnlyCollection<InitiativeStatusUpdateResponse> StatusUpdates,
    bool CanUpdateStatus,
    int StatusUpdateRetentionLimit = ProjectHistoryRetentionPolicy.MaximumInitiativeStatusUpdates);

public sealed record PortfolioProjectDependencyResponse(
    string Id,
    string SourceProjectId,
    string TargetProjectId,
    string Description,
    string Status,
    DateTimeOffset? RequiredBy);

public sealed record PortfolioResponse(
    string Id,
    string OwnerUserId,
    string Name,
    string? Description,
    IReadOnlyCollection<string> ViewerUserIds,
    IReadOnlyCollection<InitiativeResponse> Initiatives,
    IReadOnlyCollection<PortfolioProjectDependencyResponse> Dependencies,
    bool CanEdit,
    bool Archived,
    DateTimeOffset UpdatedAt,
    long Version) : IVersionedResource;

public sealed record PortfolioPageResponse(
    IReadOnlyCollection<PortfolioResponse> Items,
    int Page,
    int PageSize,
    long Total);

public sealed record PortfolioProjectMilestoneSource(
    string Id,
    string Name,
    DateTimeOffset DueAt,
    string Status,
    DateTimeOffset? CompletedAt);

public sealed record PortfolioProjectSource(
    string Id,
    string Key,
    string Name,
    int TotalWorkItems,
    int CompletedWorkItems,
    int OverdueWorkItems,
    IReadOnlyCollection<PortfolioProjectMilestoneSource> Milestones,
    DateTimeOffset UpdatedAt);

public sealed record PortfolioProjectSourceResult(
    IReadOnlyCollection<PortfolioProjectSource> Projects,
    IReadOnlyCollection<string> UnavailableProjectIds);

public sealed record PortfolioRoadmapProjectResponse(
    string Id,
    string Key,
    string Name,
    int TotalWorkItems,
    int CompletedWorkItems,
    int OverdueWorkItems,
    int Progress,
    IReadOnlyCollection<PortfolioProjectMilestoneSource> Milestones,
    DateTimeOffset UpdatedAt);

public sealed record PortfolioRoadmapInitiativeResponse(
    string Id,
    string Name,
    string? ParentInitiativeId,
    string OwnerUserId,
    string Status,
    string Health,
    int? Confidence,
    DateTimeOffset? TargetAt,
    int TotalWorkItems,
    int CompletedWorkItems,
    int OverdueWorkItems,
    int Progress,
    IReadOnlyCollection<PortfolioRoadmapProjectResponse> Projects);

public sealed record PortfolioRoadmapResponse(
    string PortfolioId,
    string SourceStatus,
    DateTimeOffset GeneratedAt,
    IReadOnlyCollection<string> UnavailableProjectIds,
    IReadOnlyCollection<PortfolioRoadmapInitiativeResponse> Initiatives,
    IReadOnlyCollection<PortfolioProjectDependencyResponse> Dependencies);

public interface IPortfolioDirectory
{
    Task EnsureOrganizationUsersAsync(
        string organizationId,
        IReadOnlyCollection<string> userIds,
        CancellationToken ct);

    Task EnsureProjectsManageableAsync(
        string organizationId,
        IReadOnlyCollection<string> projectIds,
        CancellationToken ct);

    Task EnsureMilestoneLinksAsync(
        string organizationId,
        IReadOnlyCollection<PortfolioMilestoneLinkRequest> milestoneLinks,
        CancellationToken ct);

    Task<PortfolioProjectSourceResult> ReadProjectSourcesAsync(
        string organizationId,
        IReadOnlyCollection<string> projectIds,
        CancellationToken ct);
}

public interface IPortfolioAuditWriter
{
    Task WriteAsync(
        string action,
        string portfolioId,
        string? oldValue,
        string? newValue,
        string correlationId,
        CancellationToken ct);
}

public sealed class PortfolioService(
    IDocumentRepository<PortfolioDocument> portfolios,
    IPortfolioDirectory directory,
    IPortfolioAuditWriter audit,
    ICurrentUser currentUser,
    IClock clock,
    IExpectedVersionAccessor? expectedVersions = null)
{
    private const int MaximumInitiatives = 100;
    private const int MaximumProjectsPerInitiative = 20;
    private const int MaximumDependencies = 200;
    private const int MaximumHierarchyDepth = 5;
    private readonly ExpectedVersionState expectedVersion = new(expectedVersions);

    public async Task<PortfolioResponse> SaveAsync(
        string? portfolioId,
        SavePortfolioRequest request,
        string correlationId,
        CancellationToken ct)
    {
        var actor = CurrentActor();
        var viewers = NormalizeIds(request.ViewerUserIds, 50, "Portfolio viewer");
        viewers.Remove(actor.UserId);
        await directory.EnsureOrganizationUsersAsync(
            actor.OrganizationId,
            viewers.Append(actor.UserId).ToList(),
            ct);
        var now = clock.UtcNow;
        PortfolioDocument portfolio;
        if (string.IsNullOrWhiteSpace(portfolioId))
        {
            portfolio = new PortfolioDocument
            {
                OrganizationId = actor.OrganizationId,
                OwnerUserId = actor.UserId,
                CreatedAt = now
            };
            Apply(portfolio, request, viewers, now);
            portfolio = await portfolios.CreateAsync(portfolio, ct);
            await audit.WriteAsync(
                "PortfolioCreated", portfolio.Id, null, portfolio.Name, correlationId, ct);
        }
        else
        {
            portfolio = await GetDocumentAsync(portfolioId, includeArchived: false, ct);
            EnsureOwner(portfolio, actor.UserId);
            var oldValue = portfolio.Name;
            Apply(portfolio, request, viewers, now);
            await ReplaceAsync(portfolio, ct);
            await audit.WriteAsync(
                "PortfolioUpdated", portfolio.Id, oldValue, portfolio.Name, correlationId, ct);
        }
        return ToResponse(portfolio, actor.UserId);
    }

    public async Task<PortfolioPageResponse> ListAsync(
        bool includeArchived,
        int page,
        int pageSize,
        CancellationToken ct)
    {
        var actor = CurrentActor();
        var visible = new List<PortfolioDocument>();
        string? cursor = null;
        do
        {
            var batch = await portfolios.ListByCursorAsync(
                item => item.OrganizationId == actor.OrganizationId
                    && (includeArchived || !item.Archived),
                cursor,
                100,
                ct);
            visible.AddRange(batch.Items.Where(item =>
                item.OwnerUserId == actor.UserId || item.ViewerUserIds.Contains(actor.UserId)));
            cursor = batch.NextCursor;
        } while (cursor is not null);

        var normalizedPage = Math.Max(page, 1);
        var normalizedPageSize = Math.Clamp(pageSize, 1, 100);
        var ordered = visible
            .OrderByDescending(item => item.UpdatedAt)
            .ThenBy(item => item.Id, StringComparer.Ordinal)
            .ToList();
        return new PortfolioPageResponse(
            ordered.Skip((normalizedPage - 1) * normalizedPageSize)
                .Take(normalizedPageSize)
                .Select(item => ToResponse(item, actor.UserId))
                .ToList(),
            normalizedPage,
            normalizedPageSize,
            ordered.Count);
    }

    public async Task<PortfolioResponse> GetAsync(
        string portfolioId,
        bool includeArchived,
        CancellationToken ct)
    {
        var actor = CurrentActor();
        var portfolio = await GetDocumentAsync(portfolioId, includeArchived, ct);
        EnsureVisible(portfolio, actor.UserId);
        return ToResponse(portfolio, actor.UserId);
    }

    public async Task<PortfolioResponse> SaveInitiativeAsync(
        string portfolioId,
        string? initiativeId,
        SaveInitiativeRequest request,
        string correlationId,
        CancellationToken ct)
    {
        var actor = CurrentActor();
        var portfolio = await GetDocumentAsync(portfolioId, includeArchived: false, ct);
        EnsureOwner(portfolio, actor.UserId);
        var normalized = Normalize(request);
        await directory.EnsureOrganizationUsersAsync(
            portfolio.OrganizationId,
            [normalized.OwnerUserId],
            ct);
        await directory.EnsureProjectsManageableAsync(
            portfolio.OrganizationId,
            normalized.ProjectIds,
            ct);
        await directory.EnsureMilestoneLinksAsync(
            portfolio.OrganizationId,
            normalized.MilestoneLinks,
            ct);

        InitiativeDocument initiative;
        if (string.IsNullOrWhiteSpace(initiativeId))
        {
            if (portfolio.Initiatives.Count >= MaximumInitiatives)
                throw new ValidationException($"A portfolio cannot contain more than {MaximumInitiatives} initiatives.");
            initiative = new InitiativeDocument();
            portfolio.Initiatives.Add(initiative);
        }
        else
        {
            initiative = portfolio.Initiatives.SingleOrDefault(item => item.Id == initiativeId)
                ?? throw new NotFoundException("INITIATIVE_NOT_FOUND", "Initiative was not found.");
        }
        Apply(initiative, normalized);
        ValidateHierarchy(portfolio.Initiatives);
        portfolio.UpdatedAt = clock.UtcNow;
        await ReplaceAsync(portfolio, ct);
        await audit.WriteAsync(
            string.IsNullOrWhiteSpace(initiativeId) ? "InitiativeCreated" : "InitiativeUpdated",
            portfolio.Id,
            null,
            initiative.Name,
            correlationId,
            ct);
        return ToResponse(portfolio, actor.UserId);
    }

    public async Task<PortfolioResponse> AddStatusUpdateAsync(
        string portfolioId,
        string initiativeId,
        AddInitiativeStatusUpdateRequest request,
        string correlationId,
        CancellationToken ct)
    {
        var actor = CurrentActor();
        var portfolio = await GetDocumentAsync(portfolioId, includeArchived: false, ct);
        EnsureVisible(portfolio, actor.UserId);
        var initiative = portfolio.Initiatives.SingleOrDefault(item => item.Id == initiativeId)
            ?? throw new NotFoundException("INITIATIVE_NOT_FOUND", "Initiative was not found.");
        if (portfolio.OwnerUserId != actor.UserId && initiative.OwnerUserId != actor.UserId)
            throw new ForbiddenException("Only the portfolio or initiative owner can publish a status update.");
        var status = Allowed(request.Status, InitiativeStatuses.Allowed, "Initiative status");
        var health = Allowed(request.Health, InitiativeHealth.Allowed, "Initiative health");
        var confidence = Confidence(request.Confidence);
        var note = Required(request.Note, "Status update note", 1000);
        initiative.Status = status;
        initiative.Health = health;
        initiative.Confidence = confidence;
        initiative.StatusUpdates.Insert(0, new InitiativeStatusUpdateDocument
        {
            Status = status,
            Health = health,
            Confidence = confidence,
            Note = note,
            AuthorUserId = actor.UserId,
            CreatedAt = clock.UtcNow
        });
        ProjectHistoryRetentionPolicy.RetainMostRecent(
            initiative.StatusUpdates,
            ProjectHistoryRetentionPolicy.MaximumInitiativeStatusUpdates);
        portfolio.UpdatedAt = clock.UtcNow;
        await ReplaceAsync(portfolio, ct);
        await audit.WriteAsync(
            "InitiativeStatusUpdated", portfolio.Id, null, initiative.Id, correlationId, ct);
        return ToResponse(portfolio, actor.UserId);
    }

    public async Task<PortfolioResponse> SaveDependencyAsync(
        string portfolioId,
        string? dependencyId,
        SavePortfolioDependencyRequest request,
        string correlationId,
        CancellationToken ct)
    {
        var actor = CurrentActor();
        var portfolio = await GetDocumentAsync(portfolioId, includeArchived: false, ct);
        EnsureOwner(portfolio, actor.UserId);
        var source = Required(request.SourceProjectId, "Source project", 128);
        var target = Required(request.TargetProjectId, "Target project", 128);
        if (source == target)
            throw new ValidationException("A project cannot depend on itself.");
        await directory.EnsureProjectsManageableAsync(
            portfolio.OrganizationId,
            [source, target],
            ct);
        if (!portfolio.Initiatives.Any(item => item.ProjectIds.Contains(source))
            || !portfolio.Initiatives.Any(item => item.ProjectIds.Contains(target)))
        {
            throw new ValidationException("Dependency projects must be linked to portfolio initiatives.");
        }

        PortfolioProjectDependencyDocument dependency;
        if (string.IsNullOrWhiteSpace(dependencyId))
        {
            if (portfolio.Dependencies.Count >= MaximumDependencies)
                throw new ValidationException($"A portfolio cannot contain more than {MaximumDependencies} dependencies.");
            dependency = new PortfolioProjectDependencyDocument();
            portfolio.Dependencies.Add(dependency);
        }
        else
        {
            dependency = portfolio.Dependencies.SingleOrDefault(item => item.Id == dependencyId)
                ?? throw new NotFoundException("PORTFOLIO_DEPENDENCY_NOT_FOUND", "Portfolio dependency was not found.");
        }
        dependency.SourceProjectId = source;
        dependency.TargetProjectId = target;
        dependency.Description = Required(request.Description, "Dependency description", 500);
        dependency.Status = Allowed(
            request.Status,
            PortfolioDependencyStatuses.Allowed,
            "Dependency status");
        dependency.RequiredBy = request.RequiredBy;
        ValidateDependencyGraph(portfolio.Dependencies);
        portfolio.UpdatedAt = clock.UtcNow;
        await ReplaceAsync(portfolio, ct);
        await audit.WriteAsync(
            string.IsNullOrWhiteSpace(dependencyId)
                ? "PortfolioDependencyCreated"
                : "PortfolioDependencyUpdated",
            portfolio.Id,
            null,
            dependency.Id,
            correlationId,
            ct);
        return ToResponse(portfolio, actor.UserId);
    }

    public async Task<PortfolioRoadmapResponse> GetRoadmapAsync(
        string portfolioId,
        CancellationToken ct)
    {
        var actor = CurrentActor();
        var portfolio = await GetDocumentAsync(portfolioId, includeArchived: false, ct);
        EnsureVisible(portfolio, actor.UserId);
        var projectIds = portfolio.Initiatives
            .SelectMany(item => item.ProjectIds)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        var source = await directory.ReadProjectSourcesAsync(
            portfolio.OrganizationId,
            projectIds,
            ct);
        var byId = source.Projects.ToDictionary(item => item.Id, StringComparer.Ordinal);
        var initiatives = portfolio.Initiatives.Select(initiative =>
        {
            var projects = initiative.ProjectIds
                .Where(byId.ContainsKey)
                .Select(projectId =>
                {
                    var project = byId[projectId];
                    return new PortfolioRoadmapProjectResponse(
                        project.Id,
                        project.Key,
                        project.Name,
                        project.TotalWorkItems,
                        project.CompletedWorkItems,
                        project.OverdueWorkItems,
                        Progress(project.CompletedWorkItems, project.TotalWorkItems),
                        project.Milestones,
                        project.UpdatedAt);
                })
                .ToList();
            var total = projects.Sum(item => item.TotalWorkItems);
            var completed = projects.Sum(item => item.CompletedWorkItems);
            return new PortfolioRoadmapInitiativeResponse(
                initiative.Id,
                initiative.Name,
                initiative.ParentInitiativeId,
                initiative.OwnerUserId,
                initiative.Status,
                initiative.Health,
                initiative.Confidence,
                initiative.TargetAt,
                total,
                completed,
                projects.Sum(item => item.OverdueWorkItems),
                Progress(completed, total),
                projects);
        }).ToList();
        return new PortfolioRoadmapResponse(
            portfolio.Id,
            source.UnavailableProjectIds.Count == 0
                ? PortfolioSourceStatuses.Ready
                : PortfolioSourceStatuses.Partial,
            clock.UtcNow,
            source.UnavailableProjectIds,
            initiatives,
            portfolio.Dependencies.Select(ToResponse).ToList());
    }

    public async Task ArchiveAsync(
        string portfolioId,
        string correlationId,
        CancellationToken ct)
    {
        var actor = CurrentActor();
        var portfolio = await GetDocumentAsync(portfolioId, includeArchived: false, ct);
        EnsureOwner(portfolio, actor.UserId);
        portfolio.Archived = true;
        portfolio.UpdatedAt = clock.UtcNow;
        await ReplaceAsync(portfolio, ct);
        await audit.WriteAsync(
            "PortfolioArchived", portfolio.Id, "Active", "Archived", correlationId, ct);
    }

    private async Task<PortfolioDocument> GetDocumentAsync(
        string portfolioId,
        bool includeArchived,
        CancellationToken ct)
    {
        var actor = CurrentActor();
        return await portfolios.SelectAsync(
            item => item.Id == portfolioId
                && item.OrganizationId == actor.OrganizationId
                && (includeArchived || !item.Archived),
            ct)
            ?? throw new NotFoundException("PORTFOLIO_NOT_FOUND", "Portfolio was not found.");
    }

    private async Task ReplaceAsync(PortfolioDocument portfolio, CancellationToken ct)
    {
        var result = await portfolios.ReplaceByVersionAsync(
            item => item.Id == portfolio.Id && item.OrganizationId == portfolio.OrganizationId,
            portfolio,
            expectedVersion.Consume(portfolio.Version),
            ct);
        if (!result.Found)
            throw new NotFoundException("PORTFOLIO_NOT_FOUND", "Portfolio was not found.");
        portfolio.Version = result.Version!.Value;
    }

    private (string UserId, string OrganizationId) CurrentActor() => (
        currentUser.UserId ?? throw new UnauthorizedException("Authenticated user is required."),
        currentUser.OrganizationId ?? throw new UnauthorizedException("Active organization is required."));

    private static SaveInitiativeRequest Normalize(SaveInitiativeRequest request)
    {
        var projectIds = NormalizeIds(
            request.ProjectIds,
            MaximumProjectsPerInitiative,
            "Initiative project");
        var links = (request.MilestoneLinks
                ?? throw new ValidationException("Initiative milestone links are required."))
            .Select(link => new PortfolioMilestoneLinkRequest(
                Required(link.ProjectId, "Milestone project", 128),
                Required(link.MilestoneId, "Milestone", 128)))
            .Distinct()
            .ToList();
        if (links.Count > 50)
            throw new ValidationException("An initiative cannot contain more than 50 milestone links.");
        if (links.Any(link => !projectIds.Contains(link.ProjectId)))
            throw new ValidationException("Milestone links must belong to an initiative project.");
        return request with
        {
            Name = Required(request.Name, "Initiative name", 120),
            Summary = Optional(request.Summary, 1000),
            ParentInitiativeId = Optional(request.ParentInitiativeId, 128),
            OwnerUserId = Required(request.OwnerUserId, "Initiative owner", 128),
            Status = Allowed(request.Status, InitiativeStatuses.Allowed, "Initiative status"),
            Health = Allowed(request.Health, InitiativeHealth.Allowed, "Initiative health"),
            Confidence = Confidence(request.Confidence),
            ProjectIds = projectIds,
            MilestoneLinks = links
        };
    }

    private static void Apply(
        PortfolioDocument portfolio,
        SavePortfolioRequest request,
        IReadOnlyCollection<string> viewers,
        DateTimeOffset now)
    {
        portfolio.Name = Required(request.Name, "Portfolio name", 120);
        portfolio.Description = Optional(request.Description, 1000);
        portfolio.ViewerUserIds = viewers.ToList();
        portfolio.UpdatedAt = now;
    }

    private static void Apply(InitiativeDocument initiative, SaveInitiativeRequest request)
    {
        initiative.Name = request.Name;
        initiative.Summary = request.Summary;
        initiative.ParentInitiativeId = request.ParentInitiativeId;
        initiative.OwnerUserId = request.OwnerUserId;
        initiative.Status = request.Status;
        initiative.Health = request.Health;
        initiative.Confidence = request.Confidence;
        initiative.TargetAt = request.TargetAt;
        initiative.ProjectIds = request.ProjectIds.ToList();
        initiative.MilestoneLinks = request.MilestoneLinks.Select(link =>
            new PortfolioMilestoneLinkDocument
            {
                ProjectId = link.ProjectId,
                MilestoneId = link.MilestoneId
            }).ToList();
    }

    private static void ValidateHierarchy(IReadOnlyCollection<InitiativeDocument> initiatives)
    {
        var byId = initiatives.ToDictionary(item => item.Id, StringComparer.Ordinal);
        foreach (var initiative in initiatives)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal) { initiative.Id };
            var current = initiative;
            var depth = 1;
            while (current.ParentInitiativeId is not null)
            {
                if (!byId.TryGetValue(current.ParentInitiativeId, out current!))
                    throw new ValidationException("Parent initiative must belong to the same portfolio.");
                if (!seen.Add(current.Id))
                    throw new ValidationException("Initiative hierarchy cannot contain cycles.");
                depth++;
                if (depth > MaximumHierarchyDepth)
                    throw new ValidationException($"Initiative hierarchy cannot exceed {MaximumHierarchyDepth} levels.");
            }
        }
    }

    private static void ValidateDependencyGraph(
        IReadOnlyCollection<PortfolioProjectDependencyDocument> dependencies)
    {
        var active = dependencies
            .Where(item => item.Status == PortfolioDependencyStatuses.Active)
            .ToList();
        if (active.GroupBy(
                item => $"{item.SourceProjectId}\u001f{item.TargetProjectId}",
                StringComparer.Ordinal).Any(group => group.Count() > 1))
        {
            throw new ValidationException("Active portfolio dependencies must be unique.");
        }
        var targets = active
            .GroupBy(item => item.SourceProjectId, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.Select(item => item.TargetProjectId).ToList(),
                StringComparer.Ordinal);
        foreach (var start in targets.Keys)
        {
            var visiting = new HashSet<string>(StringComparer.Ordinal);
            var visited = new HashSet<string>(StringComparer.Ordinal);
            if (HasCycle(start, targets, visiting, visited))
                throw new ValidationException("Active portfolio dependencies cannot contain cycles.");
        }
    }

    private static bool HasCycle(
        string node,
        IReadOnlyDictionary<string, List<string>> targets,
        ISet<string> visiting,
        ISet<string> visited)
    {
        if (visited.Contains(node)) return false;
        if (!visiting.Add(node)) return true;
        if (targets.TryGetValue(node, out var next)
            && next.Any(target => HasCycle(target, targets, visiting, visited)))
        {
            return true;
        }
        visiting.Remove(node);
        visited.Add(node);
        return false;
    }

    private static void EnsureVisible(PortfolioDocument portfolio, string userId)
    {
        if (portfolio.OwnerUserId != userId
            && !portfolio.ViewerUserIds.Contains(userId, StringComparer.Ordinal))
        {
            throw new NotFoundException("PORTFOLIO_NOT_FOUND", "Portfolio was not found.");
        }
    }

    private static void EnsureOwner(PortfolioDocument portfolio, string userId)
    {
        EnsureVisible(portfolio, userId);
        if (portfolio.OwnerUserId != userId)
            throw new ForbiddenException("Only the portfolio owner can change this portfolio.");
    }

    private static PortfolioResponse ToResponse(PortfolioDocument item, string userId) => new(
        item.Id,
        item.OwnerUserId,
        item.Name,
        item.Description,
        item.ViewerUserIds,
        item.Initiatives.Select(initiative => ToResponse(
            initiative,
            item.OwnerUserId == userId || initiative.OwnerUserId == userId)).ToList(),
        item.Dependencies.Select(ToResponse).ToList(),
        item.OwnerUserId == userId,
        item.Archived,
        item.UpdatedAt,
        item.Version);

    private static InitiativeResponse ToResponse(
        InitiativeDocument item,
        bool canUpdateStatus) => new(
        item.Id,
        item.Name,
        item.Summary,
        item.ParentInitiativeId,
        item.OwnerUserId,
        item.Status,
        item.Health,
        item.Confidence,
        item.TargetAt,
        item.ProjectIds,
        item.MilestoneLinks.Select(link =>
            new PortfolioMilestoneLinkResponse(link.ProjectId, link.MilestoneId)).ToList(),
        item.StatusUpdates.Select(update => new InitiativeStatusUpdateResponse(
            update.Id,
            update.Status,
            update.Health,
            update.Confidence,
            update.Note,
            update.AuthorUserId,
            update.CreatedAt)).ToList(),
        canUpdateStatus,
        ProjectHistoryRetentionPolicy.MaximumInitiativeStatusUpdates);

    private static PortfolioProjectDependencyResponse ToResponse(
        PortfolioProjectDependencyDocument item) => new(
        item.Id,
        item.SourceProjectId,
        item.TargetProjectId,
        item.Description,
        item.Status,
        item.RequiredBy);

    private static int Progress(int completed, int total) =>
        total <= 0 ? 0 : Math.Clamp((int)Math.Round(completed * 100d / total), 0, 100);

    private static List<string> NormalizeIds(
        IReadOnlyCollection<string>? values,
        int maximum,
        string label)
    {
        var normalized = (values ?? throw new ValidationException($"{label} list is required."))
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToList();
        if (normalized.Count > maximum || normalized.Any(value => value.Length > 128))
            throw new ValidationException($"{label} list is outside the supported bounds.");
        return normalized;
    }

    private static string Allowed(string? value, IReadOnlySet<string> allowed, string label)
    {
        var normalized = Required(value, label, 32);
        return allowed.Contains(normalized)
            ? normalized
            : throw new ValidationException($"{label} is not supported.");
    }

    private static int? Confidence(int? value)
    {
        if (value is < 0 or > 100)
            throw new ValidationException("Initiative confidence must be between 0 and 100.");
        return value;
    }

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
        if (string.IsNullOrWhiteSpace(value)) return null;
        var normalized = value.Trim();
        if (normalized.Length > maximum)
            throw new ValidationException($"Value cannot exceed {maximum} characters.");
        return normalized;
    }
}
