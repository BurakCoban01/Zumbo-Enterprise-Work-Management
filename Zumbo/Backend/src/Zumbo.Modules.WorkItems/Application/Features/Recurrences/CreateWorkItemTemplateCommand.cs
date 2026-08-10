namespace Zumbo.Modules.WorkItems.Application.Features.Recurrences;

public sealed record CreateWorkItemTemplateCommand(
    CreateWorkItemTemplateRequest Request,
    string CorrelationId);
