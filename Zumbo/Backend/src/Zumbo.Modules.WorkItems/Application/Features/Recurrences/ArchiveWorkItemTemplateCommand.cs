namespace Zumbo.Modules.WorkItems.Application.Features.Recurrences;

public sealed record ArchiveWorkItemTemplateCommand(
    string TemplateId,
    string CorrelationId);
