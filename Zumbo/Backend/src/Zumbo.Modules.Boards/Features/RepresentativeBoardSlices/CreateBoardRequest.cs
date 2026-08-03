using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Boards;

public sealed record CreateBoardRequest(string ProjectId, string Name, string Type);
