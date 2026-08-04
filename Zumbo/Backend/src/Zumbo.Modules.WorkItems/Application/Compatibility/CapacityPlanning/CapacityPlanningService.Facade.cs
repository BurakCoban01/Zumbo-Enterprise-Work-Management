using Zumbo.Modules.WorkItems.Application.Features.CapacityPlanning;
using Zumbo.Modules.WorkItems.Application.Features.CapacityPlanning.Scenarios;
using Zumbo.Modules.WorkItems.Application.Features.CapacityPlanning.Snapshots;

namespace Zumbo.Modules.WorkItems;

public sealed partial class CapacityPlanningService{

    public async Task ArchiveAsync(
        string planId,
        string correlationId,
        CancellationToken ct)
        => await archiveHandler.HandleAsync(
            new ArchiveCapacityPlanCommand(planId, correlationId),
            ct);

    public async Task<CapacityPlanResponse> GetAsync(
        string planId,
        bool includeArchived,
        CancellationToken ct)
        => await getHandler.HandleAsync(
            new GetCapacityPlanQuery(planId, includeArchived),
            ct);

    public async Task<CapacitySnapshotResponse> GetSnapshotAsync(
        string planId,
        CancellationToken ct)
        => await snapshotHandler.HandleAsync(
            new GetCapacitySnapshotQuery(planId),
            ct);

    public async Task<CapacityPlanPageResponse> ListAsync(
        bool includeArchived,
        int page,
        int pageSize,
        CancellationToken ct)
        => await listHandler.HandleAsync(
            new ListCapacityPlansQuery(includeArchived, page, pageSize),
            ct);

    public async Task<CapacityScenarioResponse> PreviewScenarioAsync(
        string planId,
        CapacityScenarioRequest request,
        CancellationToken ct)
        => await scenarioHandler.HandleAsync(
            new PreviewScenarioQuery(planId, request),
            ct);

    public async Task<CapacityPlanResponse> SaveAsync(
        string? planId,
        SaveCapacityPlanRequest request,
        string correlationId,
        CancellationToken ct)
        => await saveHandler.HandleAsync(
            new SaveCapacityPlanCommand(planId, request, correlationId),
            ct);

    public async Task<CapacityPlanResponse> ShareAsync(
        string planId,
        ShareCapacityPlanRequest request,
        string correlationId,
        CancellationToken ct)
        => await shareHandler.HandleAsync(
            new ShareCapacityPlanCommand(planId, request, correlationId),
            ct);
}
