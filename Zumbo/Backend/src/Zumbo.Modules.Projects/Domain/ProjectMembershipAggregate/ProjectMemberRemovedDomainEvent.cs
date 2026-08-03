using Zumbo.BuildingBlocks.Application.Events;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Projects;

public sealed record ProjectMemberRemovedDomainEvent(
    string ProjectId,
    string UserId,
    string Role,
    DateTimeOffset OccurredAt) : IDomainEvent;
