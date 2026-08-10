using Zumbo.Modules.WorkItems.Application.Features.Recurrences;

namespace Zumbo.Modules.WorkItems;

public sealed partial class WorkItemTemplateRecurrenceService
{
    public async Task<WorkItemRecurrenceResponse> CreateRecurrenceAsync(
        CreateWorkItemRecurrenceRequest request,
        string correlationId,
        CancellationToken ct) =>
        await createWorkItemRecurrenceHandler.HandleAsync(
            new CreateWorkItemRecurrenceCommand(request, correlationId),
            ct);

    public async Task<WorkItemRecurrencePreviewResponse> PreviewRecurrenceAsync(
        PreviewWorkItemRecurrenceRequest request,
        CancellationToken ct) =>
        await previewWorkItemRecurrenceHandler.HandleAsync(
            new PreviewWorkItemRecurrenceQuery(request),
            ct);

    public async Task<WorkItemRecurrenceResponse> SetRecurrenceStateAsync(
        string recurrenceId,
        bool active,
        string correlationId,
        CancellationToken ct) =>
        await setWorkItemRecurrenceStateHandler.HandleAsync(
            new SetWorkItemRecurrenceStateCommand(recurrenceId, active, correlationId),
            ct);

    public async Task ArchiveRecurrenceAsync(
        string recurrenceId,
        string correlationId,
        CancellationToken ct) =>
        await archiveWorkItemRecurrenceHandler.HandleAsync(
            new ArchiveWorkItemRecurrenceCommand(recurrenceId, correlationId),
            ct);

    public async Task<WorkItemRecurrencePage> ListRecurrencesAsync(
        string projectId,
        int page,
        int pageSize,
        bool includeArchived,
        CancellationToken ct) =>
        await listWorkItemRecurrencesHandler.HandleAsync(
            new ListWorkItemRecurrencesQuery(projectId, page, pageSize, includeArchived),
            ct);

    public async Task<WorkItemRecurrenceOccurrencePage> ListOccurrencesAsync(
        string recurrenceId,
        int page,
        int pageSize,
        CancellationToken ct) =>
        await listRecurrenceOccurrencesHandler.HandleAsync(
            new ListRecurrenceOccurrencesQuery(recurrenceId, page, pageSize),
            ct);
}
