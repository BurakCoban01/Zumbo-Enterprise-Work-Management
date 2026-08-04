using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Boards;
public sealed record CreateBoardViewRequest(
    string Name,
    bool IsShared,
    string SwimlaneMode,
    BoardFilterRequest Filter);
