namespace Zumbo.Modules.WorkItems.Application.Features.Development.Mappings;

public sealed record DeleteMappingCommand(
    string MappingId,
    long ExpectedVersion,
    string CorrelationId);
