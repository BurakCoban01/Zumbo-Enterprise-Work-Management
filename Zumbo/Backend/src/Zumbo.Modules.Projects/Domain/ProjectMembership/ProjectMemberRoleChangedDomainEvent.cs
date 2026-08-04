using Zumbo.BuildingBlocks.Application.Events;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Projects;

public sealed record ProjectMemberRoleChangedDomainEvent(
    string ProjectId,
    string UserId,
    string PreviousRole,
    string Role,
    DateTimeOffset OccurredAt) : IDomainEvent;
