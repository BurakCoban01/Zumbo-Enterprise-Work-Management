using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Projects;

public sealed record InitiativeStatusUpdateResponse(
    string Id,
    string Status,
    string Health,
    int? Confidence,
    string Note,
    string AuthorUserId,
    DateTimeOffset CreatedAt);
