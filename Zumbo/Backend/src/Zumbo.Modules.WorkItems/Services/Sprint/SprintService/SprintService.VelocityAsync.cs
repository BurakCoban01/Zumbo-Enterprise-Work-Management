using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.Modules.WorkItems.Domain;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

public sealed partial class SprintService{

    public async Task<IReadOnlyList<SprintVelocityResponse>> VelocityAsync(
        string projectId,
        int sprintCount,
        CancellationToken ct) =>
        (await VelocitySnapshotAsync(projectId, sprintCount, ct)).Data;
}
