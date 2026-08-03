using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Organizations;

public sealed class CreateOrganizationHandler(OrganizationService service)
{
    private CreateOrganizationSlice? slice;

    public CreateOrganizationHandler(
        IDocumentRepository<OrganizationDocument> organizations,
        IDistributedLockProvider distributedLockProvider,
        IOptions<DistributedLockOptions> distributedLockOptions,
        IClock clock,
        ICurrentUser currentUser,
        IOrganizationAuditWriter audit)
        : this(null!)
    {
        slice = new CreateOrganizationSlice(
            organizations,
            distributedLockProvider,
            distributedLockOptions,
            clock,
            currentUser,
            audit);
    }

    public Task<OrganizationResponse> HandleAsync(
        CreateOrganizationRequest request,
        string correlationId,
        CancellationToken ct) =>
        slice?.HandleAsync(request, correlationId, ct)
        ?? service.CreateAsync(request, correlationId, ct);
}
