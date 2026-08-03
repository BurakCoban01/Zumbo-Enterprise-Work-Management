using Zumbo.BuildingBlocks.Application.Events;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Projects;

public sealed record ProjectMemberRemovedIntegrationEvent(
    string EventId,
    string AggregateId,
    string UserId,
    string Role,
    DateTimeOffset OccurredAt) : IIntegrationEvent
{
    public string EventName => "project.member-removed.v1";
}
