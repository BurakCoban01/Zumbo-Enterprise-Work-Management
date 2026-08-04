namespace Zumbo.Modules.WorkItems.Application.Features.Development.Credentials;

public sealed record RotateWebhookSecretCommand(
    string ConnectionId,
    DevelopmentVersionRequest Request,
    string CorrelationId);
