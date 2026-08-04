namespace Zumbo.Modules.Boards.Application.Features.Columns;

public sealed record DeleteColumnCommand(
    string BoardId,
    string ColumnId,
    string CorrelationId);
