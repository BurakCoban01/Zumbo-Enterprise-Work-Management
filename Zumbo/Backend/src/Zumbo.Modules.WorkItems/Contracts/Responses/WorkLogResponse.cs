using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;
public sealed record WorkLogResponse(string Id, string UserId, decimal Hours, string? Note, DateTimeOffset CreatedAt);
