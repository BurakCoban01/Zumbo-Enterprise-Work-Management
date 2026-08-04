using Zumbo.BuildingBlocks.Application.Events;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Workflows;

public sealed record WorkflowDefinedDomainEvent(
    string WorkflowId,
    string ProjectId,
    int StatusCount,
    int TransitionCount,
    DateTimeOffset OccurredAt) : IDomainEvent;
