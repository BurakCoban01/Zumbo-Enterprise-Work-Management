using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Projects;

public sealed record SaveKeyResultRequest(
    string Name,
    string? Description,
    string OwnerUserId,
    decimal BaselineValue,
    decimal TargetValue,
    decimal InitialValue,
    string Unit,
    string Direction);
