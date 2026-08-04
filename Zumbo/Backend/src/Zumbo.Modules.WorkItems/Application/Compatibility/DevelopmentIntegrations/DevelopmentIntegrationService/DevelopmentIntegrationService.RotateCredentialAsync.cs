using Zumbo.Modules.WorkItems.Application.Features.Development.Credentials;

namespace Zumbo.Modules.WorkItems;

public sealed partial class DevelopmentIntegrationService{

    public async Task<DevelopmentConnectionResponse> RotateCredentialAsync(
        string connectionId,
        RotateDevelopmentCredentialRequest request,
        string correlationId,
        CancellationToken ct)
        => await rotateCredentialHandler.HandleAsync(
            new RotateCredentialCommand(connectionId, request, correlationId),
            ct);

}
