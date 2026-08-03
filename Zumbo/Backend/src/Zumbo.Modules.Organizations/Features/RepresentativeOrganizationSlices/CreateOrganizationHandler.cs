using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Organizations;

public sealed class CreateOrganizationHandler(OrganizationService service)
{
    public Task<OrganizationResponse> HandleAsync(
        CreateOrganizationRequest request,
        string correlationId,
        CancellationToken ct) =>
        service.CreateAsync(request, correlationId, ct);
}
