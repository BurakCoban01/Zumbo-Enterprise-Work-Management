using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Projects;

public sealed record KeyResultProgressUpdateResponse(
    string Id,
    decimal PreviousValue,
    decimal CurrentValue,
    int? Confidence,
    string Note,
    string AuthorUserId,
    DateTimeOffset CreatedAt);
