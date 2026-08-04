namespace Zumbo.Modules.WorkItems.Application.Features.Development.Credentials;

public sealed record RotateCredentialCommand(
    string ConnectionId,
    RotateDevelopmentCredentialRequest Request,
    string CorrelationId);
