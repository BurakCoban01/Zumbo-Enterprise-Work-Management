using Zumbo.BuildingBlocks.Application.Events;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Projects;

public sealed record ProjectMemberRoleChangedIntegrationEvent(
    string EventId,
    string AggregateId,
    string UserId,
    string PreviousRole,
    string Role,
    DateTimeOffset OccurredAt) : IIntegrationEvent
{
    public string EventName => "project.member-role-changed.v1";
}
