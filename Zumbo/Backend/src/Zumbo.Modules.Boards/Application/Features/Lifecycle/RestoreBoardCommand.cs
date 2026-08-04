namespace Zumbo.Modules.Boards.Application.Features.Lifecycle;

public sealed record RestoreBoardCommand(string BoardId, string CorrelationId);
