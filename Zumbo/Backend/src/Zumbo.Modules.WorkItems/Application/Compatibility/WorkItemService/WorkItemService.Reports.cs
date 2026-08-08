using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

public sealed partial class WorkItemService
{
    public async Task<ProjectSummaryResponse> ProjectSummaryAsync(string projectId, CancellationToken ct) =>
        (await projectSummaryHandler.HandleAsync(new ProjectSummaryQuery(projectId), ct)).Data;

    public async Task<WorkItemReportSnapshot<ProjectSummaryResponse>> ProjectSummarySnapshotAsync(
        string projectId,
        CancellationToken ct)
        => await projectSummaryHandler.HandleAsync(new ProjectSummaryQuery(projectId), ct);

    public async Task<IReadOnlyList<StatusDistributionResponse>> StatusDistributionAsync(
        string projectId,
        CancellationToken ct) =>
        (await statusDistributionHandler.HandleAsync(new StatusDistributionQuery(projectId), ct)).Data;

    public async Task<WorkItemReportSnapshot<IReadOnlyList<StatusDistributionResponse>>> StatusDistributionSnapshotAsync(
        string projectId,
        CancellationToken ct) =>
        await statusDistributionHandler.HandleAsync(new StatusDistributionQuery(projectId), ct);

    public async Task<IReadOnlyList<UserWorkloadResponse>> UserWorkloadAsync(
        string projectId,
        CancellationToken ct) =>
        (await userWorkloadHandler.HandleAsync(new UserWorkloadQuery(projectId), ct)).Data;

    public async Task<WorkItemReportSnapshot<IReadOnlyList<UserWorkloadResponse>>> UserWorkloadSnapshotAsync(
        string projectId,
        CancellationToken ct) =>
        await userWorkloadHandler.HandleAsync(new UserWorkloadQuery(projectId), ct);

    public async Task<IReadOnlyList<DueDateRiskResponse>> DueDateRisksAsync(
        string projectId,
        int days,
        CancellationToken ct) =>
        (await dueDateRisksHandler.HandleAsync(new DueDateRisksQuery(projectId, days), ct)).Data;

    public async Task<WorkItemReportSnapshot<IReadOnlyList<DueDateRiskResponse>>> DueDateRisksSnapshotAsync(
        string projectId,
        int days,
        CancellationToken ct) =>
        await dueDateRisksHandler.HandleAsync(new DueDateRisksQuery(projectId, days), ct);

    public async Task<FlowTimeReportResponse> FlowTimeAsync(
        string projectId,
        DateOnly? from,
        DateOnly? to,
        CancellationToken ct) =>
        (await flowTimeHandler.HandleAsync(new FlowTimeQuery(projectId, from, to), ct)).Data;

    public async Task<WorkItemReportSnapshot<FlowTimeReportResponse>> FlowTimeSnapshotAsync(
        string projectId,
        DateOnly? from,
        DateOnly? to,
        CancellationToken ct) =>
        await flowTimeHandler.HandleAsync(new FlowTimeQuery(projectId, from, to), ct);

    public async Task<TaskCompletionRateResponse> CompletionRateAsync(
        string projectId,
        DateOnly? from,
        DateOnly? to,
        CancellationToken ct) =>
        (await completionRateHandler.HandleAsync(
            new CompletionRateQuery(projectId, from, to),
            ct)).Data;

    public async Task<WorkItemReportSnapshot<TaskCompletionRateResponse>> CompletionRateSnapshotAsync(
        string projectId,
        DateOnly? from,
        DateOnly? to,
        CancellationToken ct) =>
        await completionRateHandler.HandleAsync(
            new CompletionRateQuery(projectId, from, to),
            ct);

    public async Task<IReadOnlyList<TeamPerformanceResponse>> TeamPerformanceAsync(
        string projectId,
        DateOnly? from,
        DateOnly? to,
        CancellationToken ct) =>
        (await teamPerformanceHandler.HandleAsync(
            new TeamPerformanceQuery(projectId, from, to),
            ct)).Data;

    public async Task<WorkItemReportSnapshot<IReadOnlyList<TeamPerformanceResponse>>> TeamPerformanceSnapshotAsync(
        string projectId,
        DateOnly? from,
        DateOnly? to,
        CancellationToken ct) =>
        await teamPerformanceHandler.HandleAsync(
            new TeamPerformanceQuery(projectId, from, to),
            ct);
}
