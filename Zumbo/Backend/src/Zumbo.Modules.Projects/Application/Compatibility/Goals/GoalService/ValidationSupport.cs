using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Projects;

public sealed partial class GoalService{

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

    private sealed record NormalizedGoalRequest(
        string Name,
        string? Description,
        DateOnly PeriodStart,
        DateOnly PeriodEnd,
        List<string> ViewerUserIds,
        List<GoalInitiativeLinkRequest> InitiativeLinks,
        List<string> ProjectIds);

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
}
