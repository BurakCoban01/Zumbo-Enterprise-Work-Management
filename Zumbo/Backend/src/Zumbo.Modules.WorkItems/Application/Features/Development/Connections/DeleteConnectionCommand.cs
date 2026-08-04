namespace Zumbo.Modules.WorkItems.Application.Features.Development.Connections;

public sealed record DeleteConnectionCommand(
    string ConnectionId,
    long ExpectedVersion,
    string CorrelationId);
