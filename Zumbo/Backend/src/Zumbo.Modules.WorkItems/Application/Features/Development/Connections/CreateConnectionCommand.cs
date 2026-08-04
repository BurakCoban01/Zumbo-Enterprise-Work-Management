namespace Zumbo.Modules.WorkItems.Application.Features.Development.Connections;

public sealed record CreateConnectionCommand(
    CreateDevelopmentConnectionRequest Request,
    string CorrelationId);
