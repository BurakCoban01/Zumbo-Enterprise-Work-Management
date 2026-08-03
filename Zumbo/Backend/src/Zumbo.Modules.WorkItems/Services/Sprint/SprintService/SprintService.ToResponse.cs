using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.Modules.WorkItems.Domain;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

public sealed partial class SprintService{

    private static SprintResponse ToResponse(SprintDocument sprint) =>
        new(
            sprint.Id,
            sprint.ProjectId,
            sprint.Name,
            sprint.Goal,
            DateOnly.FromDateTime(sprint.StartAtUtc.UtcDateTime),
            DateOnly.FromDateTime(sprint.EndAtUtc.UtcDateTime),
            sprint.Status,
            sprint.CommittedItems,
            sprint.CommittedPoints,
            sprint.CompletedItems,
            sprint.CompletedPoints,
            sprint.CarryoverItems,
            sprint.CarryoverPoints,
            sprint.StartedAt,
            sprint.CompletedAt,
            sprint.Version);
}
