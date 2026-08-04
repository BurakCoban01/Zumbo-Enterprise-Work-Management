using Zumbo.BuildingBlocks.Application.Events;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Projects;

public sealed record ProjectMemberAddedDomainEvent(
    string ProjectId,
    string OrganizationId,
    string UserId,
    string Role,
    DateTimeOffset OccurredAt) : IDomainEvent;
