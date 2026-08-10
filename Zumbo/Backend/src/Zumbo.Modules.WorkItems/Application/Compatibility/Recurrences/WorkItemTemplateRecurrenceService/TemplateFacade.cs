using Zumbo.Modules.WorkItems.Application.Features.Recurrences;

namespace Zumbo.Modules.WorkItems;

public sealed partial class WorkItemTemplateRecurrenceService
{
    public async Task<WorkItemTemplateResponse> CreateTemplateAsync(
        CreateWorkItemTemplateRequest request,
        string correlationId,
        CancellationToken ct) =>
        await createWorkItemTemplateHandler.HandleAsync(
            new CreateWorkItemTemplateCommand(request, correlationId),
            ct);

    public async Task<WorkItemTemplateResponse> UpdateTemplateAsync(
        string templateId,
        UpdateWorkItemTemplateRequest request,
        string correlationId,
        CancellationToken ct) =>
        await updateWorkItemTemplateHandler.HandleAsync(
            new UpdateWorkItemTemplateCommand(templateId, request, correlationId),
            ct);

    public async Task ArchiveTemplateAsync(
        string templateId,
        string correlationId,
        CancellationToken ct) =>
        await archiveWorkItemTemplateHandler.HandleAsync(
            new ArchiveWorkItemTemplateCommand(templateId, correlationId),
            ct);

    public async Task<WorkItemTemplatePage> ListTemplatesAsync(
        string projectId,
        int page,
        int pageSize,
        bool includeArchived,
        CancellationToken ct) =>
        await listWorkItemTemplatesHandler.HandleAsync(
            new ListWorkItemTemplatesQuery(projectId, page, pageSize, includeArchived),
            ct);
}
