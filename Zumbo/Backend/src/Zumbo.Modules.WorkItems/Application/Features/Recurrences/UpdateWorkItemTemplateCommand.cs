namespace Zumbo.Modules.WorkItems.Application.Features.Recurrences;

public sealed record UpdateWorkItemTemplateCommand(
    string TemplateId,
    UpdateWorkItemTemplateRequest Request,
    string CorrelationId);
