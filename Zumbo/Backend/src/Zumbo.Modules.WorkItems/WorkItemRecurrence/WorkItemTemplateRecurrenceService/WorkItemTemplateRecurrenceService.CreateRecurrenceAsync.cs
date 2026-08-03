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

    public async Task<WorkItemRecurrenceResponse> CreateRecurrenceAsync(
        CreateWorkItemRecurrenceRequest request,
        string correlationId,
        CancellationToken ct)
    {
        var authorization = await EnsurePermissionAsync(request.ProjectId, PermissionCatalog.WorkItemCreate, ct);
        var template = await GetTemplateAsync(request.TemplateId, includeArchived: false, ct);
        EnsureOwnership(template.OrganizationId, template.ProjectId, authorization.OrganizationId, request.ProjectId);
        var schedule = ValidateSchedule(request);
        var now = clock.UtcNow;
        var recurrence = await recurrences.CreateAsync(new WorkItemRecurrenceDocument
        {
            OrganizationId = authorization.OrganizationId,
            ProjectId = request.ProjectId,
            TemplateId = template.Id,
            Frequency = schedule.Frequency,
            Interval = schedule.Interval,
            StartAtUtc = schedule.StartAtUtc,
            EndAtUtc = schedule.EndAtUtc,
            NextRunAtUtc = schedule.StartAtUtc,
            MaxOccurrences = schedule.MaxOccurrences,
            CreatedByUserId = RequireCurrentUser(),
            CreatedAt = now,
            UpdatedAt = now
        }, ct);
        await audit.WriteAsync(
            "WorkItemRecurrenceCreated", "WorkItemRecurrence", recurrence.Id, null,
            $"{recurrence.Frequency}:{recurrence.Interval}", correlationId, ct);
        return await ToResponseAsync(recurrence, ct);
    }
}
