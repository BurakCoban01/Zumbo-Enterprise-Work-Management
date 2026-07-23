using Zumbo.Modules.Audit;
using Zumbo.Modules.WorkItems;

public sealed class OperationsStorageSecurityCoordinator(
    AttachmentSecurityMaintenanceService service,
    AuditService audit)
{
    public Task<AttachmentSecurityStatus> GetStatusAsync(
        string organizationId,
        CancellationToken ct) =>
        service.GetStatusAsync(organizationId, ct);

    public async Task<AttachmentMaintenanceResult> RunAsync(
        string organizationId,
        string correlationId,
        CancellationToken ct)
    {
        var result = await service.RunBatchAsync(organizationId, ct);
        await audit.WriteAsync(
            "AttachmentSecurityMaintenanceRun",
            "Organization",
            organizationId,
            null,
            $"{result.Retried}:{result.Cleaned}:{result.Rejected}:{result.PurgedMetadata}",
            correlationId,
            ct);
        return result;
    }
}
