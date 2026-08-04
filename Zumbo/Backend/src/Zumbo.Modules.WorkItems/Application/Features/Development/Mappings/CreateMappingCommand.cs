namespace Zumbo.Modules.WorkItems.Application.Features.Development.Mappings;

public sealed record CreateMappingCommand(
    string ConnectionId,
    CreateDevelopmentRepositoryMappingRequest Request,
    string CorrelationId);
