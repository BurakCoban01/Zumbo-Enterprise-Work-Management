namespace Zumbo.Modules.WorkItems.Application.Features.Development.Links;

public sealed record CreateWorkItemLinkCommand(
    string WorkItemId,
    CreateWorkItemDevelopmentLinkRequest Request,
    string CorrelationId);
