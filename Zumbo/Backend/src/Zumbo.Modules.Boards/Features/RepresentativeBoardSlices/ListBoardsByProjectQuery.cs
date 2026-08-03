using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Boards;

public sealed record ListBoardsByProjectQuery(string ProjectId, bool Archived);
