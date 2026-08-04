using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

public sealed partial class WorkItemTemplateRecurrenceService{

    public async Task<WorkItemRecurrencePreviewResponse> PreviewRecurrenceAsync(
        PreviewWorkItemRecurrenceRequest request,
        CancellationToken ct)
    {
        var authorization = await EnsurePermissionAsync(request.ProjectId, PermissionCatalog.WorkItemCreate, ct);
        var template = await GetTemplateAsync(request.TemplateId, includeArchived: false, ct);
        EnsureOwnership(template.OrganizationId, template.ProjectId, authorization.OrganizationId, request.ProjectId);
        var schedule = ValidateSchedule(new CreateWorkItemRecurrenceRequest(
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
            next = Next(next, schedule.Frequency, schedule.Interval);
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
