using Zumbo.BuildingBlocks.Application.Events;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

public sealed record SprintAssignment(string? SprintId, decimal EstimatePoints) : ValueObject
{
    public static SprintAssignment Create(
        string? sprintId,
        decimal? requestedEstimatePoints,
        decimal currentEstimatePoints)
    {
        if (requestedEstimatePoints is < 0 or > 1000)
        {
            throw new ValidationException("Estimate points must be between 0 and 1000.");
        }

        return new(
            string.IsNullOrWhiteSpace(sprintId) ? null : sprintId.Trim(),
            requestedEstimatePoints ?? currentEstimatePoints);
    }
}
