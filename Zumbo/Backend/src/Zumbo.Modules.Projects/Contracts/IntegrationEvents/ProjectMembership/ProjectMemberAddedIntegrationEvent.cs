using Zumbo.BuildingBlocks.Application.Events;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Projects;

public sealed record ProjectMemberAddedIntegrationEvent(
    string EventId,
    string AggregateId,
    string OrganizationId,
    string UserId,
    string Role,
    DateTimeOffset OccurredAt) : IIntegrationEvent
{
    public string EventName => "project.member-added.v1";
}
