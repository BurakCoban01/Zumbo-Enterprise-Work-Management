using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.Modules.WorkItems;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems.Application.Features.Recurrences;

internal sealed class CreateWorkItemRecurrenceSlice(
    IDocumentRepository<WorkItemTemplateDocument> templates,
    IDocumentRepository<WorkItemRecurrenceDocument> recurrences,
    IDocumentRepository<WorkItemRecurrenceOccurrenceDocument> occurrences,
    IProjectPermissionChecker permissionChecker,
    ICurrentUser currentUser,
    IOptions<WorkItemRecurrenceOptions> options,
    IClock clock,
    IWorkItemAuditPublisher audit)
{
    private readonly WorkItemTemplateReadAccess access =
        new(templates, permissionChecker, currentUser);
    private readonly RecurrenceSchedulePolicy schedulePolicy = new(options, clock);
    private readonly RecurrenceResponseMapper mapper = new(occurrences);

    internal async Task<WorkItemRecurrenceResponse> HandleAsync(
        CreateWorkItemRecurrenceCommand command,
        CancellationToken ct)
    {
        var request = command.Request;
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
        var schedule = schedulePolicy.Validate(request);
        var now = clock.UtcNow;
        var userId = currentUser.UserId
            ?? throw new UnauthorizedException("Authenticated user is required.");
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
            CreatedByUserId = userId,
            CreatedAt = now,
            UpdatedAt = now
        }, ct);
        await audit.WriteAsync(
            "WorkItemRecurrenceCreated",
            "WorkItemRecurrence",
            recurrence.Id,
            null,
            $"{recurrence.Frequency}:{recurrence.Interval}",
            command.CorrelationId,
            ct);
        return await mapper.ToResponseAsync(recurrence, ct);
    }
}
