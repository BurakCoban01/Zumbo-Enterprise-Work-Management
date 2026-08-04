namespace Zumbo.Modules.WorkItems.Application.Features.Development.Connections;

public sealed record DisconnectConnectionCommand(
    string ConnectionId,
    DevelopmentVersionRequest Request,
    string CorrelationId);
