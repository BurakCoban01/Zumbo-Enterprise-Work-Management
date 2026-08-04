namespace Zumbo.Modules.WorkItems.Application.Features.Development.ProviderHealth;

public sealed record CheckProviderHealthCommand(
    string ConnectionId,
    string CorrelationId);
