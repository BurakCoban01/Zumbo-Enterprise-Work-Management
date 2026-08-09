using Zumbo.SharedKernel;

namespace Zumbo.Modules.Projects.Application.Features.Goals;

internal static class GoalRequestNormalizer
{
    private const int MaximumViewers = 50;
    private const int MaximumInitiativeLinks = 20;
    private const int MaximumProjectLinks = 20;

    internal static NormalizedGoalRequest Normalize(SaveGoalRequest request)
    {
        if (request.PeriodEnd < request.PeriodStart)
            throw new ValidationException("Goal period end cannot be before its start.");
        if (request.PeriodStart.AddYears(5) < request.PeriodEnd)
            throw new ValidationException("Goal period cannot exceed five years.");
        var links = (request.InitiativeLinks
                ?? throw new ValidationException("Goal initiative links are required."))
            .Select(item => new GoalInitiativeLinkRequest(
                GoalValidation.Required(item.PortfolioId, "Portfolio", 128),
                GoalValidation.Required(item.InitiativeId, "Initiative", 128)))
            .Distinct()
            .ToList();
        if (links.Count > MaximumInitiativeLinks)
            throw new ValidationException("A goal cannot link more than 20 initiatives.");
        return new NormalizedGoalRequest(
            GoalValidation.Required(request.Name, "Goal name", 160),
            GoalValidation.Optional(request.Description, 2000),
            request.PeriodStart,
            request.PeriodEnd,
            NormalizeIds(request.ViewerUserIds, MaximumViewers, "Goal viewer"),
            links,
            NormalizeIds(request.ProjectIds, MaximumProjectLinks, "Goal project"));
    }

    internal static SaveKeyResultRequest Normalize(SaveKeyResultRequest request)
    {
        GoalValidation.EnsureFinite(request.BaselineValue, "Key-result baseline");
        GoalValidation.EnsureFinite(request.TargetValue, "Key-result target");
        GoalValidation.EnsureFinite(request.InitialValue, "Key-result initial value");
        var direction = GoalValidation.Allowed(
            request.Direction, KeyResultDirections.Allowed, "Key-result direction");
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
            Name = GoalValidation.Required(request.Name, "Key-result name", 160),
            Description = GoalValidation.Optional(request.Description, 1000),
            OwnerUserId = GoalValidation.Required(request.OwnerUserId, "Key-result owner", 128),
            Unit = GoalValidation.Required(request.Unit, "Key-result unit", 32),
            Direction = direction
        };
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
}
