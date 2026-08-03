using Zumbo.BuildingBlocks.Application.Persistence;

namespace Zumbo.Modules.Organizations;

public interface IOrganizationAuditWriter
{
    Task WriteAsync(
        string action,
        string organizationId,
        string? oldValue,
        string? newValue,
        string correlationId,
        CancellationToken ct);
}
