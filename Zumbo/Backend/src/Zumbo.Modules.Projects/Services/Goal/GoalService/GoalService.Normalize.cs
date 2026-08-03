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
}
