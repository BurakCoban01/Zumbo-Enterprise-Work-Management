namespace Zumbo.Modules.Boards;

public sealed record BoardViewResponse(
    string Id,
    string Name,
    string OwnerUserId,
    bool IsShared,
    string SwimlaneMode,
    BoardFilterResponse Filter);
