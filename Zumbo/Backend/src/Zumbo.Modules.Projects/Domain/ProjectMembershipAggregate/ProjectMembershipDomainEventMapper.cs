using Zumbo.BuildingBlocks.Application.Events;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Projects;

public sealed class ProjectMembershipDomainEventMapper :
    IIntegrationEventMapper<ProjectMemberAddedDomainEvent, ProjectMemberAddedIntegrationEvent>,
    IIntegrationEventMapper<ProjectMemberRoleChangedDomainEvent, ProjectMemberRoleChangedIntegrationEvent>,
    IIntegrationEventMapper<ProjectMemberRemovedDomainEvent, ProjectMemberRemovedIntegrationEvent>
{
    public ProjectMemberAddedIntegrationEvent Map(ProjectMemberAddedDomainEvent domainEvent) =>
        new(
            Guid.NewGuid().ToString("N"),
            domainEvent.ProjectId,
            domainEvent.OrganizationId,
            domainEvent.UserId,
            domainEvent.Role,
            domainEvent.OccurredAt);

    public ProjectMemberRoleChangedIntegrationEvent Map(ProjectMemberRoleChangedDomainEvent domainEvent) =>
        new(
            Guid.NewGuid().ToString("N"),
            domainEvent.ProjectId,
            domainEvent.UserId,
            domainEvent.PreviousRole,
            domainEvent.Role,
            domainEvent.OccurredAt);

    public ProjectMemberRemovedIntegrationEvent Map(ProjectMemberRemovedDomainEvent domainEvent) =>
        new(
            Guid.NewGuid().ToString("N"),
            domainEvent.ProjectId,
            domainEvent.UserId,
            domainEvent.Role,
            domainEvent.OccurredAt);
}
