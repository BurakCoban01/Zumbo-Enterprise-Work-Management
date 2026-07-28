using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Projects;

public sealed record GoalInitiativeLinkRequest(string PortfolioId, string InitiativeId);

public sealed record SaveGoalRequest(
    string Name,
    string? Description,
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    IReadOnlyCollection<string> ViewerUserIds,
    IReadOnlyCollection<GoalInitiativeLinkRequest> InitiativeLinks,
    IReadOnlyCollection<string> ProjectIds);

public sealed record SaveKeyResultRequest(
    string Name,
    string? Description,
    string OwnerUserId,
    decimal BaselineValue,
    decimal TargetValue,
    decimal InitialValue,
    string Unit,
    string Direction);

public sealed record AddKeyResultProgressRequest(
    decimal CurrentValue,
    int? Confidence,
    string Note);

public sealed record AddGoalStatusUpdateRequest(
    string Status,
    string Health,
    int? Confidence,
    string Note);

public sealed record GoalInitiativeLinkResponse(string PortfolioId, string InitiativeId);

public sealed record KeyResultProgressUpdateResponse(
    string Id,
    decimal PreviousValue,
    decimal CurrentValue,
    int? Confidence,
    string Note,
    string AuthorUserId,
    DateTimeOffset CreatedAt);

public sealed record KeyResultResponse(
    string Id,
    string OwnerUserId,
    string Name,
    string? Description,
    decimal BaselineValue,
    decimal TargetValue,
    decimal CurrentValue,
    string Unit,
    string Direction,
    int Progress,
    int? Confidence,
    IReadOnlyCollection<KeyResultProgressUpdateResponse> ProgressUpdates,
    bool CanUpdate,
    int ProgressUpdateRetentionLimit = ProjectHistoryRetentionPolicy.MaximumKeyResultProgressUpdates);

public sealed record GoalStatusUpdateResponse(
    string Id,
    string Status,
    string Health,
    int? Confidence,
    string Note,
    string AuthorUserId,
    DateTimeOffset CreatedAt);

public sealed record GoalResponse(
    string Id,
    string OwnerUserId,
    string Name,
    string? Description,
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    string Status,
    string Health,
    int? Confidence,
    int Progress,
    IReadOnlyCollection<string> ViewerUserIds,
    IReadOnlyCollection<GoalInitiativeLinkResponse> InitiativeLinks,
    IReadOnlyCollection<string> ProjectIds,
    IReadOnlyCollection<KeyResultResponse> KeyResults,
    IReadOnlyCollection<GoalStatusUpdateResponse> StatusUpdates,
    bool CanEdit,
    bool CanUpdateStatus,
    bool Archived,
    DateTimeOffset UpdatedAt,
    long Version,
    int StatusUpdateRetentionLimit = ProjectHistoryRetentionPolicy.MaximumGoalStatusUpdates) : IVersionedResource;

public sealed record GoalPageResponse(
    IReadOnlyCollection<GoalResponse> Items,
    int Page,
    int PageSize,
    long Total);

public sealed record GoalInitiativeSource(
    string PortfolioId,
    string Id,
    string Name,
    string Status,
    string Health,
    int? Confidence);

public sealed record GoalProjectSource(string Id, string Key, string Name);

public sealed record GoalSourceResult(
    IReadOnlyCollection<GoalInitiativeSource> Initiatives,
    IReadOnlyCollection<GoalProjectSource> Projects,
    IReadOnlyCollection<string> UnavailableSources);

public sealed record GoalRollupResponse(
    string GoalId,
    string SourceStatus,
    int Progress,
    int? Confidence,
    DateTimeOffset GeneratedAt,
    IReadOnlyCollection<GoalInitiativeSource> Initiatives,
    IReadOnlyCollection<GoalProjectSource> Projects,
    IReadOnlyCollection<string> UnavailableSources);

public interface IGoalDirectory
{
    Task EnsureOrganizationUsersAsync(
        string organizationId,
        IReadOnlyCollection<string> userIds,
        CancellationToken ct);

    Task EnsureSourcesReadableAsync(
        string organizationId,
        IReadOnlyCollection<GoalInitiativeLinkRequest> initiativeLinks,
        IReadOnlyCollection<string> projectIds,
        CancellationToken ct);

    Task<GoalSourceResult> ReadSourcesAsync(
        string organizationId,
        IReadOnlyCollection<GoalInitiativeLinkRequest> initiativeLinks,
        IReadOnlyCollection<string> projectIds,
        CancellationToken ct);
}

public interface IGoalAuditWriter
{
    Task WriteAsync(
        string action,
        string goalId,
        string? oldValue,
        string? newValue,
        string correlationId,
        CancellationToken ct);
}

public sealed class GoalService(
    IDocumentRepository<GoalDocument> goals,
    IGoalDirectory directory,
    IGoalAuditWriter audit,
    ICurrentUser currentUser,
    IClock clock,
    IExpectedVersionAccessor? expectedVersions = null)
{
    private const int MaximumViewers = 50;
    private const int MaximumInitiativeLinks = 20;
    private const int MaximumProjectLinks = 20;
    private const int MaximumKeyResults = 50;
    private readonly ExpectedVersionState expectedVersion = new(expectedVersions);

    public async Task<GoalResponse> SaveAsync(
        string? goalId,
        SaveGoalRequest request,
        string correlationId,
        CancellationToken ct)
    {
        var actor = CurrentActor();
        var normalized = Normalize(request);
        normalized.ViewerUserIds.Remove(actor.UserId);
        await directory.EnsureOrganizationUsersAsync(
            actor.OrganizationId,
            normalized.ViewerUserIds.Append(actor.UserId).ToList(),
            ct);
        await directory.EnsureSourcesReadableAsync(
            actor.OrganizationId,
            normalized.InitiativeLinks,
            normalized.ProjectIds,
            ct);

        GoalDocument goal;
        var now = clock.UtcNow;
        if (string.IsNullOrWhiteSpace(goalId))
        {
            goal = new GoalDocument
            {
                OrganizationId = actor.OrganizationId,
                OwnerUserId = actor.UserId,
                CreatedAt = now
            };
            Apply(goal, normalized, now);
            goal = await goals.CreateAsync(goal, ct);
            await audit.WriteAsync(
                "GoalCreated", goal.Id, null, goal.Name, correlationId, ct);
        }
        else
        {
            goal = await GetDocumentAsync(goalId, includeArchived: false, ct);
            EnsureOwner(goal, actor.UserId);
            var oldName = goal.Name;
            Apply(goal, normalized, now);
            await ReplaceAsync(goal, ct);
            await audit.WriteAsync(
                "GoalUpdated", goal.Id, oldName, goal.Name, correlationId, ct);
        }
        return ToResponse(goal, actor.UserId);
    }

    public async Task<GoalPageResponse> ListAsync(
        bool includeArchived,
        int page,
        int pageSize,
        CancellationToken ct)
    {
        var actor = CurrentActor();
        var visible = new List<GoalDocument>();
        string? cursor = null;
        do
        {
            var batch = await goals.ListByCursorAsync(
                item => item.OrganizationId == actor.OrganizationId
                    && (includeArchived || !item.Archived),
                cursor,
                100,
                ct);
            visible.AddRange(batch.Items.Where(item => CanView(item, actor.UserId)));
            cursor = batch.NextCursor;
        } while (cursor is not null);

        var normalizedPage = Math.Max(page, 1);
        var normalizedPageSize = Math.Clamp(pageSize, 1, 100);
        var ordered = visible
            .OrderByDescending(item => item.UpdatedAt)
            .ThenBy(item => item.Id, StringComparer.Ordinal)
            .ToList();
        return new GoalPageResponse(
            ordered.Skip((normalizedPage - 1) * normalizedPageSize)
                .Take(normalizedPageSize)
                .Select(item => ToResponse(item, actor.UserId))
                .ToList(),
            normalizedPage,
            normalizedPageSize,
            ordered.Count);
    }

    public async Task<GoalResponse> GetAsync(
        string goalId,
        bool includeArchived,
        CancellationToken ct)
    {
        var actor = CurrentActor();
        var goal = await GetDocumentAsync(goalId, includeArchived, ct);
        EnsureVisible(goal, actor.UserId);
        return ToResponse(goal, actor.UserId);
    }

    public async Task<GoalResponse> SaveKeyResultAsync(
        string goalId,
        string? keyResultId,
        SaveKeyResultRequest request,
        string correlationId,
        CancellationToken ct)
    {
        var actor = CurrentActor();
        var goal = await GetDocumentAsync(goalId, includeArchived: false, ct);
        EnsureOwner(goal, actor.UserId);
        var normalized = Normalize(request);
        await directory.EnsureOrganizationUsersAsync(
            goal.OrganizationId,
            [normalized.OwnerUserId],
            ct);

        KeyResultDocument keyResult;
        if (string.IsNullOrWhiteSpace(keyResultId))
        {
            if (goal.KeyResults.Count >= MaximumKeyResults)
            {
                throw new ValidationException(
                    $"A goal cannot contain more than {MaximumKeyResults} key results.");
            }
            keyResult = new KeyResultDocument
            {
                CurrentValue = normalized.InitialValue
            };
            goal.KeyResults.Add(keyResult);
        }
        else
        {
            keyResult = goal.KeyResults.SingleOrDefault(item => item.Id == keyResultId)
                ?? throw new NotFoundException("KEY_RESULT_NOT_FOUND", "Key result was not found.");
        }
        Apply(keyResult, normalized);
        goal.UpdatedAt = clock.UtcNow;
        await ReplaceAsync(goal, ct);
        await audit.WriteAsync(
            string.IsNullOrWhiteSpace(keyResultId) ? "KeyResultCreated" : "KeyResultUpdated",
            goal.Id,
            null,
            keyResult.Name,
            correlationId,
            ct);
        return ToResponse(goal, actor.UserId);
    }

    public async Task<GoalResponse> AddKeyResultProgressAsync(
        string goalId,
        string keyResultId,
        AddKeyResultProgressRequest request,
        string correlationId,
        CancellationToken ct)
    {
        var actor = CurrentActor();
        var goal = await GetDocumentAsync(goalId, includeArchived: false, ct);
        EnsureVisible(goal, actor.UserId);
        var keyResult = goal.KeyResults.SingleOrDefault(item => item.Id == keyResultId)
            ?? throw new NotFoundException("KEY_RESULT_NOT_FOUND", "Key result was not found.");
        if (goal.OwnerUserId != actor.UserId && keyResult.OwnerUserId != actor.UserId)
            throw new ForbiddenException("Only the goal or key-result owner can publish progress.");
        EnsureFinite(request.CurrentValue, "Key-result current value");
        var confidence = Confidence(request.Confidence, "Key-result confidence");
        var note = Required(request.Note, "Progress note", 1000);
        var previous = keyResult.CurrentValue;
        keyResult.CurrentValue = request.CurrentValue;
        keyResult.Confidence = confidence;
        keyResult.ProgressUpdates.Insert(0, new KeyResultProgressUpdateDocument
        {
            PreviousValue = previous,
            CurrentValue = request.CurrentValue,
            Confidence = confidence,
            Note = note,
            AuthorUserId = actor.UserId,
            CreatedAt = clock.UtcNow
        });
        ProjectHistoryRetentionPolicy.RetainMostRecent(
            keyResult.ProgressUpdates,
            ProjectHistoryRetentionPolicy.MaximumKeyResultProgressUpdates);
        goal.UpdatedAt = clock.UtcNow;
        await ReplaceAsync(goal, ct);
        await audit.WriteAsync(
            "KeyResultProgressUpdated",
            goal.Id,
            previous.ToString(System.Globalization.CultureInfo.InvariantCulture),
            request.CurrentValue.ToString(System.Globalization.CultureInfo.InvariantCulture),
            correlationId,
            ct);
        return ToResponse(goal, actor.UserId);
    }

    public async Task<GoalResponse> AddStatusUpdateAsync(
        string goalId,
        AddGoalStatusUpdateRequest request,
        string correlationId,
        CancellationToken ct)
    {
        var actor = CurrentActor();
        var goal = await GetDocumentAsync(goalId, includeArchived: false, ct);
        EnsureOwner(goal, actor.UserId);
        var status = Allowed(request.Status, GoalStatuses.Allowed, "Goal status");
        var health = Allowed(request.Health, GoalHealth.Allowed, "Goal health");
        var confidence = Confidence(request.Confidence, "Goal confidence");
        var note = Required(request.Note, "Status update note", 1000);
        goal.Status = status;
        goal.Health = health;
        goal.Confidence = confidence;
        goal.StatusUpdates.Insert(0, new GoalStatusUpdateDocument
        {
            Status = status,
            Health = health,
            Confidence = confidence,
            Note = note,
            AuthorUserId = actor.UserId,
            CreatedAt = clock.UtcNow
        });
        ProjectHistoryRetentionPolicy.RetainMostRecent(
            goal.StatusUpdates,
            ProjectHistoryRetentionPolicy.MaximumGoalStatusUpdates);
        goal.UpdatedAt = clock.UtcNow;
        await ReplaceAsync(goal, ct);
        await audit.WriteAsync(
            "GoalStatusUpdated", goal.Id, null, status, correlationId, ct);
        return ToResponse(goal, actor.UserId);
    }

    public async Task<GoalRollupResponse> GetRollupAsync(
        string goalId,
        CancellationToken ct)
    {
        var actor = CurrentActor();
        var goal = await GetDocumentAsync(goalId, includeArchived: false, ct);
        EnsureVisible(goal, actor.UserId);
        var links = goal.InitiativeLinks
            .Select(item => new GoalInitiativeLinkRequest(item.PortfolioId, item.InitiativeId))
            .ToList();
        var sources = await directory.ReadSourcesAsync(
            goal.OrganizationId,
            links,
            goal.ProjectIds,
            ct);
        return new GoalRollupResponse(
            goal.Id,
            sources.UnavailableSources.Count == 0
                ? GoalSourceStatuses.Ready
                : GoalSourceStatuses.Partial,
            Progress(goal),
            goal.Confidence,
            clock.UtcNow,
            sources.Initiatives,
            sources.Projects,
            sources.UnavailableSources);
    }

    public async Task ArchiveAsync(
        string goalId,
        string correlationId,
        CancellationToken ct)
    {
        var actor = CurrentActor();
        var goal = await GetDocumentAsync(goalId, includeArchived: false, ct);
        EnsureOwner(goal, actor.UserId);
        goal.Archived = true;
        goal.UpdatedAt = clock.UtcNow;
        await ReplaceAsync(goal, ct);
        await audit.WriteAsync(
            "GoalArchived", goal.Id, "Active", "Archived", correlationId, ct);
    }

    private async Task<GoalDocument> GetDocumentAsync(
        string goalId,
        bool includeArchived,
        CancellationToken ct)
    {
        var actor = CurrentActor();
        return await goals.SelectAsync(
            item => item.Id == goalId
                && item.OrganizationId == actor.OrganizationId
                && (includeArchived || !item.Archived),
            ct)
            ?? throw new NotFoundException("GOAL_NOT_FOUND", "Goal was not found.");
    }

    private async Task ReplaceAsync(GoalDocument goal, CancellationToken ct)
    {
        var result = await goals.ReplaceByVersionAsync(
            item => item.Id == goal.Id && item.OrganizationId == goal.OrganizationId,
            goal,
            expectedVersion.Consume(goal.Version),
            ct);
        if (!result.Found)
            throw new NotFoundException("GOAL_NOT_FOUND", "Goal was not found.");
        goal.Version = result.Version!.Value;
    }

    private (string UserId, string OrganizationId) CurrentActor() => (
        currentUser.UserId ?? throw new UnauthorizedException("Authenticated user is required."),
        currentUser.OrganizationId ?? throw new UnauthorizedException("Active organization is required."));

    private static NormalizedGoalRequest Normalize(SaveGoalRequest request)
    {
        if (request.PeriodEnd < request.PeriodStart)
            throw new ValidationException("Goal period end cannot be before its start.");
        if (request.PeriodStart.AddYears(5) < request.PeriodEnd)
            throw new ValidationException("Goal period cannot exceed five years.");
        var links = (request.InitiativeLinks
                ?? throw new ValidationException("Goal initiative links are required."))
            .Select(item => new GoalInitiativeLinkRequest(
                Required(item.PortfolioId, "Portfolio", 128),
                Required(item.InitiativeId, "Initiative", 128)))
            .Distinct()
            .ToList();
        if (links.Count > MaximumInitiativeLinks)
            throw new ValidationException("A goal cannot link more than 20 initiatives.");
        return new NormalizedGoalRequest(
            Required(request.Name, "Goal name", 160),
            Optional(request.Description, 2000),
            request.PeriodStart,
            request.PeriodEnd,
            NormalizeIds(request.ViewerUserIds, MaximumViewers, "Goal viewer"),
            links,
            NormalizeIds(request.ProjectIds, MaximumProjectLinks, "Goal project"));
    }

    private static SaveKeyResultRequest Normalize(SaveKeyResultRequest request)
    {
        EnsureFinite(request.BaselineValue, "Key-result baseline");
        EnsureFinite(request.TargetValue, "Key-result target");
        EnsureFinite(request.InitialValue, "Key-result initial value");
        var direction = Allowed(
            request.Direction,
            KeyResultDirections.Allowed,
            "Key-result direction");
        if (request.BaselineValue == request.TargetValue)
            throw new ValidationException("Key-result baseline and target must differ.");
        if (direction == KeyResultDirections.Increase
            && request.TargetValue < request.BaselineValue)
        {
            throw new ValidationException(
                "An increasing key result must have a target above its baseline.");
        }
        if (direction == KeyResultDirections.Decrease
            && request.TargetValue > request.BaselineValue)
        {
            throw new ValidationException(
                "A decreasing key result must have a target below its baseline.");
        }
        return request with
        {
            Name = Required(request.Name, "Key-result name", 160),
            Description = Optional(request.Description, 1000),
            OwnerUserId = Required(request.OwnerUserId, "Key-result owner", 128),
            Unit = Required(request.Unit, "Key-result unit", 32),
            Direction = direction
        };
    }

    private static void Apply(
        GoalDocument goal,
        NormalizedGoalRequest request,
        DateTimeOffset now)
    {
        goal.Name = request.Name;
        goal.Description = request.Description;
        goal.PeriodStartAtUtc = AtStartOfDay(request.PeriodStart);
        goal.PeriodEndAtUtc = AtStartOfDay(request.PeriodEnd);
        goal.ViewerUserIds = request.ViewerUserIds;
        goal.InitiativeLinks = request.InitiativeLinks.Select(item =>
            new GoalInitiativeLinkDocument
            {
                PortfolioId = item.PortfolioId,
                InitiativeId = item.InitiativeId
            }).ToList();
        goal.ProjectIds = request.ProjectIds;
        goal.UpdatedAt = now;
    }

    private static void Apply(KeyResultDocument keyResult, SaveKeyResultRequest request)
    {
        keyResult.Name = request.Name;
        keyResult.Description = request.Description;
        keyResult.OwnerUserId = request.OwnerUserId;
        keyResult.BaselineValue = request.BaselineValue;
        keyResult.TargetValue = request.TargetValue;
        keyResult.Unit = request.Unit;
        keyResult.Direction = request.Direction;
    }

    private static bool CanView(GoalDocument goal, string userId) =>
        goal.OwnerUserId == userId
        || goal.ViewerUserIds.Contains(userId, StringComparer.Ordinal)
        || goal.KeyResults.Any(item => item.OwnerUserId == userId);

    private static void EnsureVisible(GoalDocument goal, string userId)
    {
        if (!CanView(goal, userId))
            throw new NotFoundException("GOAL_NOT_FOUND", "Goal was not found.");
    }

    private static void EnsureOwner(GoalDocument goal, string userId)
    {
        EnsureVisible(goal, userId);
        if (goal.OwnerUserId != userId)
            throw new ForbiddenException("Only the goal owner can change this goal.");
    }

    private static GoalResponse ToResponse(GoalDocument item, string userId) => new(
        item.Id,
        item.OwnerUserId,
        item.Name,
        item.Description,
        DateOnly.FromDateTime(item.PeriodStartAtUtc.UtcDateTime),
        DateOnly.FromDateTime(item.PeriodEndAtUtc.UtcDateTime),
        item.Status,
        item.Health,
        item.Confidence,
        Progress(item),
        item.ViewerUserIds,
        item.InitiativeLinks.Select(link =>
            new GoalInitiativeLinkResponse(link.PortfolioId, link.InitiativeId)).ToList(),
        item.ProjectIds,
        item.KeyResults.Select(keyResult => ToResponse(
            keyResult,
            item.OwnerUserId == userId || keyResult.OwnerUserId == userId)).ToList(),
        item.StatusUpdates.Select(update => new GoalStatusUpdateResponse(
            update.Id,
            update.Status,
            update.Health,
            update.Confidence,
            update.Note,
            update.AuthorUserId,
            update.CreatedAt)).ToList(),
        item.OwnerUserId == userId,
        item.OwnerUserId == userId,
        item.Archived,
        item.UpdatedAt,
        item.Version,
        ProjectHistoryRetentionPolicy.MaximumGoalStatusUpdates);

    private static KeyResultResponse ToResponse(
        KeyResultDocument item,
        bool canUpdate) => new(
        item.Id,
        item.OwnerUserId,
        item.Name,
        item.Description,
        item.BaselineValue,
        item.TargetValue,
        item.CurrentValue,
        item.Unit,
        item.Direction,
        Progress(item),
        item.Confidence,
        item.ProgressUpdates.Select(update => new KeyResultProgressUpdateResponse(
            update.Id,
            update.PreviousValue,
            update.CurrentValue,
            update.Confidence,
            update.Note,
            update.AuthorUserId,
            update.CreatedAt)).ToList(),
        canUpdate,
        ProjectHistoryRetentionPolicy.MaximumKeyResultProgressUpdates);

    private static int Progress(GoalDocument goal)
    {
        if (goal.KeyResults.Count == 0) return 0;
        return (int)Math.Round(goal.KeyResults.Average(Progress));
    }

    private static int Progress(KeyResultDocument keyResult)
    {
        var distance = keyResult.Direction == KeyResultDirections.Increase
            ? keyResult.TargetValue - keyResult.BaselineValue
            : keyResult.BaselineValue - keyResult.TargetValue;
        var travelled = keyResult.Direction == KeyResultDirections.Increase
            ? keyResult.CurrentValue - keyResult.BaselineValue
            : keyResult.BaselineValue - keyResult.CurrentValue;
        if (distance <= 0) return 0;
        return Math.Clamp((int)Math.Round(travelled * 100m / distance), 0, 100);
    }

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

    private static int? Confidence(int? value, string label)
    {
        if (value is < 0 or > 100)
            throw new ValidationException($"{label} must be between 0 and 100.");
        return value;
    }

    private static void EnsureFinite(decimal value, string label)
    {
        const decimal maximumMagnitude = 1_000_000_000_000m;
        if (value is < -maximumMagnitude or > maximumMagnitude)
            throw new ValidationException($"{label} is outside the supported range.");
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

    private static DateTimeOffset AtStartOfDay(DateOnly date) =>
        new(date.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);

    private sealed record NormalizedGoalRequest(
        string Name,
        string? Description,
        DateOnly PeriodStart,
        DateOnly PeriodEnd,
        List<string> ViewerUserIds,
        List<GoalInitiativeLinkRequest> InitiativeLinks,
        List<string> ProjectIds);
}
