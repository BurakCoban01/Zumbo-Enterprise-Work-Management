namespace Zumbo.Modules.Boards.Application.Features.Lifecycle;

public sealed record ArchiveBoardCommand(string BoardId, string CorrelationId);
