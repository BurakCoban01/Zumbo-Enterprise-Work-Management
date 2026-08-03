using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Workflows;

public sealed record WorkflowVersionResponse(
    int Number,
    string State,
    DateTimeOffset CreatedAt,
    DateTimeOffset? PublishedAt,
    int StatusCount,
    int TransitionCount);
