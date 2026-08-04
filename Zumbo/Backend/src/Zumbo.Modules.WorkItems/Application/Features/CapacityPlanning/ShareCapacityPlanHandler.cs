using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.Modules.WorkItems.Application.Policies.CapacityPlanning;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems.Application.Features.CapacityPlanning;

public sealed class ShareCapacityPlanHandler(
    IDocumentRepository<CapacityPlanDocument> plans,
    ICapacityPlanningDirectory directory,
    ICapacityPlanningAuditWriter audit,
    CapacityPlanAccessPolicy access,
    IClock clock,
    IExpectedVersionAccessor? expectedVersions = null)
{
    private const int MaximumViewers = 50;
    private readonly ExpectedVersionState expectedVersion = new(expectedVersions);

    public async Task<CapacityPlanResponse> HandleAsync(
        ShareCapacityPlanCommand command,
        CancellationToken ct)
    {
        var actor = access.CurrentActor();
        var plan = await plans.SelectAsync(
            item => item.Id == command.PlanId
                && item.OrganizationId == actor.OrganizationId
                && !item.Archived,
            ct) ?? throw CapacityPlanAccessPolicy.PlanNotFound();
        access.EnsureOwner(plan, actor.UserId);
        var viewers = NormalizeIds(
            command.Request.ViewerUserIds
                ?? throw new ValidationException(
                    "Capacity-plan viewer list is required."));
        if (viewers.Contains(actor.UserId, StringComparer.Ordinal))
        {
            throw new ValidationException(
                "Capacity-plan owner cannot also be a viewer.");
        }

        await directory.EnsureOrganizationUsersAndTeamsAsync(
            actor.OrganizationId,
            [],
            viewers,
            ct);
        var oldValue = string.Join(
            ",",
            plan.ViewerUserIds.Order(StringComparer.Ordinal));
        plan.ViewerUserIds = viewers;
        plan.UpdatedAt = clock.UtcNow;
        var result = await plans.ReplaceByVersionAsync(
            item => item.Id == plan.Id
                && item.OrganizationId == plan.OrganizationId,
            plan,
            expectedVersion.Consume(plan.Version),
            ct);
        if (!result.Found)
        {
            throw CapacityPlanAccessPolicy.PlanNotFound();
        }

        plan.Version = result.Version!.Value;
        await audit.WriteAsync(
            "CapacityPlanSharingUpdated",
            plan.Id,
            oldValue,
            string.Join(",", viewers.Order(StringComparer.Ordinal)),
            command.CorrelationId,
            ct);
        return CapacityPlanResponseMapper.ToResponse(plan, actor.UserId);
    }

    private static List<string> NormalizeIds(
        IReadOnlyCollection<string> values)
    {
        var result = values
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => Required(value, "Capacity-plan viewer", 128))
            .Distinct(StringComparer.Ordinal)
            .ToList();
        if (result.Count > MaximumViewers)
        {
            throw new ValidationException(
                $"Capacity-plan viewer list cannot exceed {MaximumViewers} entries.");
        }

        return result;
    }

    private static string Required(string? value, string label, int maximum)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ValidationException($"{label} is required.");
        }

        var normalized = value.Trim();
        if (normalized.Length > maximum)
        {
            throw new ValidationException(
                $"{label} cannot exceed {maximum} characters.");
        }

        return normalized;
    }
}
