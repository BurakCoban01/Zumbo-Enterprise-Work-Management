using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.Modules.WorkItems;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems.Application.Features.Recurrences;

internal sealed class PreviewWorkItemRecurrenceSlice(
    IDocumentRepository<WorkItemTemplateDocument> templates,
    IProjectPermissionChecker permissionChecker,
    ICurrentUser currentUser,
    IOptions<WorkItemRecurrenceOptions> options,
    IClock clock)
{
    private readonly WorkItemTemplateReadAccess access =
        new(templates, permissionChecker, currentUser);
    private readonly RecurrenceSchedulePolicy schedulePolicy = new(options, clock);

    internal async Task<WorkItemRecurrencePreviewResponse> HandleAsync(
        PreviewWorkItemRecurrenceQuery query,
        CancellationToken ct)
    {
        var request = query.Request;
        var authorization = await access.AuthorizeProjectAsync(
            request.ProjectId,
            PermissionCatalog.WorkItemCreate,
            ct);
        var template = await access.GetTemplateAsync(
            request.TemplateId,
            includeArchived: false,
            ct);
        WorkItemTemplateReadAccess.EnsureOwnership(
            template,
            authorization.OrganizationId,
            request.ProjectId);

        var schedule = schedulePolicy.Validate(new CreateWorkItemRecurrenceRequest(
            request.ProjectId,
            request.TemplateId,
            request.Frequency,
            request.Interval,
            request.StartAtUtc,
            request.EndAtUtc,
            request.MaxOccurrences));
        var previewCount = Math.Clamp(request.PreviewCount, 1, 10);
        var limit = Math.Min(previewCount, schedule.MaxOccurrences);
        var values = new List<DateTimeOffset>(limit);
        var next = schedule.StartAtUtc;
        while (values.Count < limit && (schedule.EndAtUtc is null || next <= schedule.EndAtUtc))
        {
            values.Add(next);
            next = RecurrenceSchedulePolicy.Next(next, schedule.Frequency, schedule.Interval);
        }

        return new WorkItemRecurrencePreviewResponse(
            schedule.Frequency,
            schedule.Interval,
            schedule.StartAtUtc,
            schedule.EndAtUtc,
            schedule.MaxOccurrences,
            values);
    }
}
