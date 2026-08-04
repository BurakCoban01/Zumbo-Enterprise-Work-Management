namespace Zumbo.Modules.WorkItems.Application.Features.Development.Links;

public sealed record DeleteWorkItemLinkCommand(
    string WorkItemId,
    string LinkId,
    long ExpectedVersion,
    string CorrelationId);
