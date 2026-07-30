using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

public static class WorkItemRecurrenceFrequencies
{
    public const string Daily = "Daily";
    public const string Weekly = "Weekly";
    public const string Monthly = "Monthly";

    public static string Normalize(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ValidationException("Recurrence frequency is required.");
        }

        return value.Trim().ToLowerInvariant() switch
        {
            "daily" => Daily,
            "weekly" => Weekly,
            "monthly" => Monthly,
            _ => throw new ValidationException("Recurrence frequency must be Daily, Weekly, or Monthly.")
        };
    }
}

public static class WorkItemRecurrenceOccurrenceStates
{
    public const string Scheduled = "Scheduled";
    public const string Generated = "Generated";
}

public sealed class WorkItemTemplateDocument : IVersionedDocument
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string OrganizationId { get; set; } = string.Empty;
    public string ProjectId { get; set; } = string.Empty;
    public string BoardId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Type { get; set; } = "Task";
    public int IssueTypeSchemaVersion { get; set; } = 1;
    public List<WorkItemCustomFieldValueDocument> CustomFields { get; set; } = [];
    public string Priority { get; set; } = "Medium";
    public string? AssigneeUserId { get; set; }
    public string? TeamId { get; set; }
    public int? DueAfterDays { get; set; }
    public List<string> Labels { get; set; } = [];
    public string CreatedByUserId { get; set; } = string.Empty;
    public bool Archived { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public long Version { get; set; }
}

public sealed class WorkItemRecurrenceDocument : IVersionedDocument
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string OrganizationId { get; set; } = string.Empty;
    public string ProjectId { get; set; } = string.Empty;
    public string TemplateId { get; set; } = string.Empty;
    public string Frequency { get; set; } = WorkItemRecurrenceFrequencies.Weekly;
    public int Interval { get; set; } = 1;
    public DateTimeOffset StartAtUtc { get; set; }
    public DateTimeOffset? EndAtUtc { get; set; }
    public DateTimeOffset? NextRunAtUtc { get; set; }
    public int MaxOccurrences { get; set; } = 100;
    public int ScheduledOccurrences { get; set; }
    public bool Active { get; set; } = true;
    public bool Archived { get; set; }
    public string CreatedByUserId { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public long Version { get; set; }
}

public sealed class WorkItemRecurrenceOccurrenceDocument : IVersionedDocument
{
    public string Id { get; set; } = string.Empty;
    public string OrganizationId { get; set; } = string.Empty;
    public string ProjectId { get; set; } = string.Empty;
    public string RecurrenceId { get; set; } = string.Empty;
    public string TemplateId { get; set; } = string.Empty;
    public DateTimeOffset ScheduledForUtc { get; set; }
    public string Status { get; set; } = WorkItemRecurrenceOccurrenceStates.Scheduled;
    public string? CreatedWorkItemId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? GeneratedAt { get; set; }
    public long Version { get; set; }
}

public sealed class WorkItemRecurrenceOptions
{
    public bool Enabled { get; init; } = true;
    public int IntervalSeconds { get; init; } = 30;
    public int BatchSize { get; init; } = 50;
    public int MaximumOccurrences { get; init; } = 1_000;
    public int MaximumScheduleYears { get; init; } = 5;
}

public sealed record CreateWorkItemTemplateRequest(
    string ProjectId,
    string BoardId,
    string Name,
    string Title,
    string? Description,
    string Type,
    string? Priority,
    string? AssigneeUserId,
    string? TeamId,
    int? DueAfterDays,
    IReadOnlyCollection<string>? Labels,
    IReadOnlyCollection<WorkItemCustomFieldValueRequest>? CustomFields);

public sealed record UpdateWorkItemTemplateRequest(
    string BoardId,
    string Name,
    string Title,
    string? Description,
    string Type,
    string? Priority,
    string? AssigneeUserId,
    string? TeamId,
    int? DueAfterDays,
    IReadOnlyCollection<string>? Labels,
    IReadOnlyCollection<WorkItemCustomFieldValueRequest>? CustomFields);

public sealed record WorkItemTemplateResponse(
    string Id,
    string ProjectId,
    string BoardId,
    string Name,
    string Title,
    string Description,
    string Type,
    string Priority,
    string? AssigneeUserId,
    string? TeamId,
    int? DueAfterDays,
    IReadOnlyCollection<string> Labels,
    int IssueTypeSchemaVersion,
    IReadOnlyCollection<WorkItemCustomFieldValueResponse> CustomFields,
    bool Archived,
    long Version) : IVersionedResource;

public sealed record WorkItemTemplatePage(
    IReadOnlyCollection<WorkItemTemplateResponse> Items,
    int Page,
    int PageSize,
    long TotalCount);

public sealed record CreateWorkItemRecurrenceRequest(
    string ProjectId,
    string TemplateId,
    string Frequency,
    int Interval,
    DateTimeOffset StartAtUtc,
    DateTimeOffset? EndAtUtc,
    int MaxOccurrences);

public sealed record PreviewWorkItemRecurrenceRequest(
    string ProjectId,
    string TemplateId,
    string Frequency,
    int Interval,
    DateTimeOffset StartAtUtc,
    DateTimeOffset? EndAtUtc,
    int MaxOccurrences,
    int PreviewCount = 5);

public sealed record SetWorkItemRecurrenceStateRequest(bool Active);

public sealed record WorkItemRecurrencePreviewResponse(
    string Frequency,
    int Interval,
    DateTimeOffset StartAtUtc,
    DateTimeOffset? EndAtUtc,
    int MaxOccurrences,
    IReadOnlyCollection<DateTimeOffset> OccurrencesUtc);

public sealed record WorkItemRecurrenceResponse(
    string Id,
    string ProjectId,
    string TemplateId,
    string Frequency,
    int Interval,
    DateTimeOffset StartAtUtc,
    DateTimeOffset? EndAtUtc,
    DateTimeOffset? NextRunAtUtc,
    int MaxOccurrences,
    int ScheduledOccurrences,
    long GeneratedOccurrences,
    bool Active,
    bool Archived,
    long Version) : IVersionedResource;

public sealed record WorkItemRecurrencePage(
    IReadOnlyCollection<WorkItemRecurrenceResponse> Items,
    int Page,
    int PageSize,
    long TotalCount);

public sealed record WorkItemRecurrenceOccurrenceResponse(
    string Id,
    DateTimeOffset ScheduledForUtc,
    string Status,
    string? CreatedWorkItemId,
    DateTimeOffset? GeneratedAt,
    long Version) : IVersionedResource;

public sealed record WorkItemRecurrenceOccurrencePage(
    IReadOnlyCollection<WorkItemRecurrenceOccurrenceResponse> Items,
    int Page,
    int PageSize,
    long TotalCount);

public sealed class WorkItemTemplateRecurrenceService(
    IDocumentRepository<WorkItemTemplateDocument> templates,
    IDocumentRepository<WorkItemRecurrenceDocument> recurrences,
    IDocumentRepository<WorkItemRecurrenceOccurrenceDocument> occurrences,
    IProjectPermissionChecker permissionChecker,
    IWorkItemTeamPolicy teamPolicy,
    IWorkItemCollaboratorDirectory collaboratorDirectory,
    IBoardPlacementPolicy boardPlacementPolicy,
    IWorkItemTypeSchemaPolicy typeSchemas,
    IWorkItemRecurrenceEventPublisher recurrencePublisher,
    IWorkItemAuditPublisher audit,
    IDistributedLockProvider distributedLocks,
    IOptions<DistributedLockOptions> lockOptions,
    IOptions<WorkItemRecurrenceOptions> configuredOptions,
    IClock clock,
    ICurrentUser currentUser,
    IExpectedVersionAccessor? expectedVersions = null)
{
    private readonly ExpectedVersionState expectedVersion = new(expectedVersions);
    private WorkItemRecurrenceOptions Options => configuredOptions.Value;

    public async Task<WorkItemTemplateResponse> CreateTemplateAsync(
        CreateWorkItemTemplateRequest request,
        string correlationId,
        CancellationToken ct)
    {
        var authorization = await EnsurePermissionAsync(request.ProjectId, PermissionCatalog.WorkItemCreate, ct);
        var normalized = await NormalizeTemplateAsync(
            request.ProjectId,
            request.BoardId,
            request.Name,
            request.Title,
            request.Description,
            request.Type,
            request.Priority,
            request.AssigneeUserId,
            request.TeamId,
            request.DueAfterDays,
            request.Labels,
            request.CustomFields,
            ct);
        await using var templateLock = await AcquireAsync("work-item-template-project:" + request.ProjectId, ct);
        await EnsureTemplateNameAvailableAsync(request.ProjectId, normalized.Name, null, ct);
        var now = clock.UtcNow;
        var template = new WorkItemTemplateDocument
        {
            OrganizationId = authorization.OrganizationId,
            ProjectId = request.ProjectId,
            BoardId = normalized.BoardId,
            Name = normalized.Name,
            Title = normalized.Title,
            Description = normalized.Description,
            Type = normalized.Type,
            IssueTypeSchemaVersion = normalized.SchemaVersion,
            CustomFields = normalized.CustomFields,
            Priority = normalized.Priority,
            AssigneeUserId = normalized.AssigneeUserId,
            TeamId = normalized.TeamId,
            DueAfterDays = normalized.DueAfterDays,
            Labels = normalized.Labels,
            CreatedByUserId = RequireCurrentUser(),
            CreatedAt = now,
            UpdatedAt = now
        };
        try
        {
            template = await templates.CreateAsync(template, ct);
        }
        catch (DocumentConflictException)
        {
            throw new ConflictException("WORK_ITEM_TEMPLATE_EXISTS", "An active template with this name already exists in the project.");
        }

        await audit.WriteAsync(
            "WorkItemTemplateCreated", "WorkItemTemplate", template.Id, null, template.Name, correlationId, ct);
        return ToResponse(template);
    }

    public async Task<WorkItemTemplateResponse> UpdateTemplateAsync(
        string templateId,
        UpdateWorkItemTemplateRequest request,
        string correlationId,
        CancellationToken ct)
    {
        await using var templateLock = await AcquireAsync("work-item-template:" + templateId, ct);
        var template = await GetTemplateAsync(templateId, includeArchived: false, ct);
        await EnsurePermissionAsync(template.ProjectId, PermissionCatalog.WorkItemUpdate, ct);
        var normalized = await NormalizeTemplateAsync(
            template.ProjectId,
            request.BoardId,
            request.Name,
            request.Title,
            request.Description,
            request.Type,
            request.Priority,
            request.AssigneeUserId,
            request.TeamId,
            request.DueAfterDays,
            request.Labels,
            request.CustomFields,
            ct);
        await EnsureTemplateNameAvailableAsync(template.ProjectId, normalized.Name, template.Id, ct);
        var oldName = template.Name;
        template.BoardId = normalized.BoardId;
        template.Name = normalized.Name;
        template.Title = normalized.Title;
        template.Description = normalized.Description;
        template.Type = normalized.Type;
        template.IssueTypeSchemaVersion = normalized.SchemaVersion;
        template.CustomFields = normalized.CustomFields;
        template.Priority = normalized.Priority;
        template.AssigneeUserId = normalized.AssigneeUserId;
        template.TeamId = normalized.TeamId;
        template.DueAfterDays = normalized.DueAfterDays;
        template.Labels = normalized.Labels;
        template.UpdatedAt = clock.UtcNow;
        var expected = expectedVersion.Consume(template.Version);
        var result = await templates.ReplaceByVersionAsync(x => x.Id == template.Id, template, expected, ct);
        if (!result.Found)
        {
            throw new ConflictException("WORK_ITEM_TEMPLATE_CONFLICT", "The template changed concurrently; reload and retry.");
        }
        template.Version = result.Version!.Value;
        await audit.WriteAsync(
            "WorkItemTemplateUpdated", "WorkItemTemplate", template.Id, oldName, template.Name, correlationId, ct);
        return ToResponse(template);
    }

    public async Task ArchiveTemplateAsync(string templateId, string correlationId, CancellationToken ct)
    {
        await using var templateLock = await AcquireAsync("work-item-template:" + templateId, ct);
        var template = await GetTemplateAsync(templateId, includeArchived: false, ct);
        await EnsurePermissionAsync(template.ProjectId, PermissionCatalog.WorkItemUpdate, ct);
        if (await recurrences.ExistsByFilterAsync(
                recurrence => recurrence.TemplateId == template.Id && !recurrence.Archived && recurrence.Active,
                ct))
        {
            throw new ConflictException(
                "WORK_ITEM_TEMPLATE_RECURRENCE_ACTIVE",
                "Pause or archive active recurrences before archiving this template.");
        }

        template.Archived = true;
        template.UpdatedAt = clock.UtcNow;
        var result = await templates.ReplaceByVersionAsync(
            x => x.Id == template.Id,
            template,
            expectedVersion.Consume(template.Version),
            ct);
        if (!result.Found)
        {
            throw new ConflictException("WORK_ITEM_TEMPLATE_CONFLICT", "The template changed concurrently; reload and retry.");
        }
        await audit.WriteAsync(
            "WorkItemTemplateArchived", "WorkItemTemplate", template.Id, template.Name, null, correlationId, ct);
    }

    public async Task<WorkItemTemplatePage> ListTemplatesAsync(
        string projectId,
        int page,
        int pageSize,
        bool includeArchived,
        CancellationToken ct)
    {
        await EnsurePermissionAsync(projectId, PermissionCatalog.WorkItemView, ct);
        var safePage = Math.Max(page, 1);
        var safeSize = Math.Clamp(pageSize, 1, 100);
        var total = await templates.CountByFilterAsync(
            template => template.ProjectId == projectId && (includeArchived || !template.Archived), ct);
        var result = await templates.ListByFilterAsync(
            template => template.ProjectId == projectId && (includeArchived || !template.Archived),
            template => template.Name,
            page: safePage,
            pageSize: safeSize,
            cancellationToken: ct);
        return new WorkItemTemplatePage(result.Select(ToResponse).ToList(), safePage, safeSize, total);
    }

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

    public async Task<WorkItemRecurrenceResponse> SetRecurrenceStateAsync(
        string recurrenceId,
        bool active,
        string correlationId,
        CancellationToken ct)
    {
        await using var recurrenceLock = await AcquireAsync("work-item-recurrence:" + recurrenceId, ct);
        var recurrence = await GetRecurrenceAsync(recurrenceId, includeArchived: false, ct);
        await EnsurePermissionAsync(recurrence.ProjectId, PermissionCatalog.WorkItemUpdate, ct);
        if (recurrence.Active == active)
        {
            throw new ConflictException("WORK_ITEM_RECURRENCE_UNCHANGED", "The recurrence state is unchanged.");
        }
        if (active && (recurrence.NextRunAtUtc is null || recurrence.ScheduledOccurrences >= recurrence.MaxOccurrences))
        {
            throw new ConflictException("WORK_ITEM_RECURRENCE_COMPLETE", "A completed recurrence cannot be resumed.");
        }

        recurrence.Active = active;
        recurrence.UpdatedAt = clock.UtcNow;
        var result = await recurrences.ReplaceByVersionAsync(
            x => x.Id == recurrence.Id,
            recurrence,
            expectedVersion.Consume(recurrence.Version),
            ct);
        if (!result.Found)
        {
            throw new ConflictException("WORK_ITEM_RECURRENCE_CONFLICT", "The recurrence changed concurrently; reload and retry.");
        }
        recurrence.Version = result.Version!.Value;
        await audit.WriteAsync(
            active ? "WorkItemRecurrenceResumed" : "WorkItemRecurrencePaused",
            "WorkItemRecurrence", recurrence.Id, (!active).ToString(), active.ToString(), correlationId, ct);
        return await ToResponseAsync(recurrence, ct);
    }

    public async Task ArchiveRecurrenceAsync(string recurrenceId, string correlationId, CancellationToken ct)
    {
        await using var recurrenceLock = await AcquireAsync("work-item-recurrence:" + recurrenceId, ct);
        var recurrence = await GetRecurrenceAsync(recurrenceId, includeArchived: false, ct);
        await EnsurePermissionAsync(recurrence.ProjectId, PermissionCatalog.WorkItemUpdate, ct);
        recurrence.Active = false;
        recurrence.Archived = true;
        recurrence.UpdatedAt = clock.UtcNow;
        var result = await recurrences.ReplaceByVersionAsync(
            x => x.Id == recurrence.Id,
            recurrence,
            expectedVersion.Consume(recurrence.Version),
            ct);
        if (!result.Found)
        {
            throw new ConflictException("WORK_ITEM_RECURRENCE_CONFLICT", "The recurrence changed concurrently; reload and retry.");
        }
        await audit.WriteAsync(
            "WorkItemRecurrenceArchived", "WorkItemRecurrence", recurrence.Id, "active", "archived", correlationId, ct);
    }

    public async Task<WorkItemRecurrencePage> ListRecurrencesAsync(
        string projectId,
        int page,
        int pageSize,
        bool includeArchived,
        CancellationToken ct)
    {
        await EnsurePermissionAsync(projectId, PermissionCatalog.WorkItemView, ct);
        var safePage = Math.Max(page, 1);
        var safeSize = Math.Clamp(pageSize, 1, 100);
        var total = await recurrences.CountByFilterAsync(
            recurrence => recurrence.ProjectId == projectId && (includeArchived || !recurrence.Archived), ct);
        var result = await recurrences.ListByFilterAsync(
            recurrence => recurrence.ProjectId == projectId && (includeArchived || !recurrence.Archived),
            recurrence => recurrence.CreatedAt,
            orderDescending: true,
            page: safePage,
            pageSize: safeSize,
            cancellationToken: ct);
        var responses = new List<WorkItemRecurrenceResponse>(result.Count);
        foreach (var recurrence in result)
        {
            responses.Add(await ToResponseAsync(recurrence, ct));
        }
        return new WorkItemRecurrencePage(responses, safePage, safeSize, total);
    }

    public async Task<WorkItemRecurrenceOccurrencePage> ListOccurrencesAsync(
        string recurrenceId,
        int page,
        int pageSize,
        CancellationToken ct)
    {
        var recurrence = await GetRecurrenceAsync(recurrenceId, includeArchived: true, ct);
        await EnsurePermissionAsync(recurrence.ProjectId, PermissionCatalog.WorkItemView, ct);
        var safePage = Math.Max(page, 1);
        var safeSize = Math.Clamp(pageSize, 1, 100);
        var total = await occurrences.CountByFilterAsync(x => x.RecurrenceId == recurrence.Id, ct);
        var result = await occurrences.ListByFilterAsync(
            x => x.RecurrenceId == recurrence.Id,
            x => x.ScheduledForUtc,
            orderDescending: true,
            page: safePage,
            pageSize: safeSize,
            cancellationToken: ct);
        return new WorkItemRecurrenceOccurrencePage(
            result.Select(ToResponse).ToList(), safePage, safeSize, total);
    }

    public async Task<int> ScheduleDueAsync(CancellationToken ct)
    {
        await using var dispatcherLock = await AcquireAsync("work-item-recurrence-scheduler", ct);
        var now = clock.UtcNow;
        var batchSize = Math.Clamp(Options.BatchSize, 1, 200);
        var candidates = await recurrences.ListByFilterAsync(
            recurrence => recurrence.Active
                && !recurrence.Archived
                && recurrence.NextRunAtUtc != null
                && recurrence.NextRunAtUtc <= now,
            recurrence => recurrence.NextRunAtUtc!,
            pageSize: batchSize,
            cancellationToken: ct);
        var scheduled = 0;
        foreach (var candidate in candidates)
        {
            await using var recurrenceLock = await AcquireAsync("work-item-recurrence:" + candidate.Id, ct);
            var recurrence = await recurrences.SelectAsync(x => x.Id == candidate.Id, ct);
            if (recurrence is null
                || !recurrence.Active
                || recurrence.Archived
                || recurrence.NextRunAtUtc is null
                || recurrence.NextRunAtUtc > now)
            {
                continue;
            }

            var template = await templates.SelectAsync(
                x => x.Id == recurrence.TemplateId
                    && x.OrganizationId == recurrence.OrganizationId
                    && x.ProjectId == recurrence.ProjectId
                    && !x.Archived,
                ct);
            if (template is null)
            {
                recurrence.Active = false;
                recurrence.UpdatedAt = now;
                await ReplaceRecurrenceAsync(recurrence, ct);
                continue;
            }

            var scheduledFor = recurrence.NextRunAtUtc.Value.ToUniversalTime();
            var occurrenceId = StableOccurrenceId(recurrence.Id, scheduledFor);
            var occurrence = await occurrences.SelectAsync(x => x.Id == occurrenceId, ct);
            if (occurrence is null)
            {
                try
                {
                    await occurrences.CreateAsync(new WorkItemRecurrenceOccurrenceDocument
                    {
                        Id = occurrenceId,
                        OrganizationId = recurrence.OrganizationId,
                        ProjectId = recurrence.ProjectId,
                        RecurrenceId = recurrence.Id,
                        TemplateId = recurrence.TemplateId,
                        ScheduledForUtc = scheduledFor,
                        CreatedAt = now
                    }, ct);
                }
                catch (DocumentConflictException)
                {
                    occurrence = await occurrences.SelectAsync(x => x.Id == occurrenceId, ct);
                    if (occurrence is null)
                    {
                        throw;
                    }
                }
            }

            await recurrencePublisher.PublishAsync(new WorkItemRecurrenceDueEvent(
                recurrence.OrganizationId,
                recurrence.ProjectId,
                recurrence.Id,
                occurrenceId,
                scheduledFor), ct);
            recurrence.ScheduledOccurrences = checked(recurrence.ScheduledOccurrences + 1);
            var next = Next(scheduledFor, recurrence.Frequency, recurrence.Interval);
            if (recurrence.ScheduledOccurrences >= recurrence.MaxOccurrences
                || recurrence.EndAtUtc is { } endAt && next > endAt)
            {
                recurrence.Active = false;
                recurrence.NextRunAtUtc = null;
            }
            else
            {
                recurrence.NextRunAtUtc = next;
            }
            recurrence.UpdatedAt = now;
            await ReplaceRecurrenceAsync(recurrence, ct);
            scheduled++;
        }

        return scheduled;
    }

    public static string StableOccurrenceId(string recurrenceId, DateTimeOffset scheduledForUtc)
    {
        var input = $"{recurrenceId}\u001f{scheduledForUtc.ToUniversalTime().UtcTicks}";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(input))).ToLowerInvariant()[..32];
    }

    private async Task<NormalizedTemplate> NormalizeTemplateAsync(
        string projectId,
        string boardId,
        string name,
        string title,
        string? description,
        string type,
        string? priority,
        string? assigneeUserId,
        string? teamId,
        int? dueAfterDays,
        IReadOnlyCollection<string>? labels,
        IReadOnlyCollection<WorkItemCustomFieldValueRequest>? customFields,
        CancellationToken ct)
    {
        var normalizedName = Required(name, "Template name", 120);
        var normalizedTitle = Required(title, "Template title", 200);
        var normalizedDescription = (description ?? string.Empty).Trim();
        if (normalizedDescription.Length > 10_000)
        {
            throw new ValidationException("Template description cannot exceed 10000 characters.");
        }
        if (string.IsNullOrWhiteSpace(boardId))
        {
            throw new ValidationException("Template board is required.");
        }
        if (dueAfterDays is < 0 or > 3_650)
        {
            throw new ValidationException("Template due offset must be between 0 and 3650 days.");
        }

        _ = await boardPlacementPolicy.ResolveInitialAsync(projectId, boardId.Trim(), ct);
        var normalizedTeam = Optional(teamId);
        var normalizedAssignee = Optional(assigneeUserId);
        if (normalizedTeam is not null)
        {
            await teamPolicy.EnsureCanAssignAsync(projectId, normalizedTeam, normalizedAssignee, ct);
        }
        else if (normalizedAssignee is not null)
        {
            var authorization = await permissionChecker.EnsureCanAsync(
                RequireCurrentUser(), projectId, PermissionCatalog.WorkItemView, ct);
            if (!await collaboratorDirectory.IsActiveProjectViewerAsync(
                    normalizedAssignee, authorization.OrganizationId, projectId, ct))
            {
                throw new ValidationException("Template assignee must be an active user who can view the project.");
            }
        }

        var shape = await typeSchemas.ValidateAsync(projectId, type, customFields, ct);
        return new NormalizedTemplate(
            boardId.Trim(),
            normalizedName,
            normalizedTitle,
            normalizedDescription,
            shape.IssueTypeKey,
            shape.SchemaVersion,
            shape.CustomFields.ToList(),
            string.IsNullOrWhiteSpace(priority) ? "Medium" : Required(priority, "Template priority", 50),
            normalizedAssignee,
            normalizedTeam,
            dueAfterDays,
            NormalizeLabels(labels));
    }

    private Schedule ValidateSchedule(CreateWorkItemRecurrenceRequest request)
    {
        var frequency = WorkItemRecurrenceFrequencies.Normalize(request.Frequency);
        if (request.Interval is < 1 or > 365)
        {
            throw new ValidationException("Recurrence interval must be between 1 and 365.");
        }
        var maximumOccurrences = Math.Clamp(Options.MaximumOccurrences, 1, 10_000);
        if (request.MaxOccurrences is < 1 || request.MaxOccurrences > maximumOccurrences)
        {
            throw new ValidationException($"Recurrence maximum occurrences must be between 1 and {maximumOccurrences}.");
        }

        var start = request.StartAtUtc.ToUniversalTime();
        var end = request.EndAtUtc?.ToUniversalTime();
        var now = clock.UtcNow;
        var maxYears = Math.Clamp(Options.MaximumScheduleYears, 1, 20);
        if (start < now.AddDays(-1) || start > now.AddYears(maxYears))
        {
            throw new ValidationException($"Recurrence start must be within one day in the past and {maxYears} years in the future.");
        }
        if (end is not null && (end < start || end > start.AddYears(maxYears)))
        {
            throw new ValidationException($"Recurrence end must follow the start and stay within {maxYears} years.");
        }

        return new Schedule(frequency, request.Interval, start, end, request.MaxOccurrences);
    }

    private async Task EnsureTemplateNameAvailableAsync(
        string projectId,
        string name,
        string? ignoredTemplateId,
        CancellationToken ct)
    {
        var normalized = name.ToLowerInvariant();
        if (await templates.ExistsByFilterAsync(
                template => template.ProjectId == projectId
                    && template.Id != ignoredTemplateId
                    && !template.Archived
                    && template.Name.ToLower() == normalized,
                ct))
        {
            throw new ConflictException("WORK_ITEM_TEMPLATE_EXISTS", "An active template with this name already exists in the project.");
        }
    }

    private async Task<WorkItemTemplateDocument> GetTemplateAsync(
        string templateId,
        bool includeArchived,
        CancellationToken ct) =>
        await templates.SelectAsync(
            template => template.Id == templateId && (includeArchived || !template.Archived), ct)
        ?? throw new NotFoundException("WORK_ITEM_TEMPLATE_NOT_FOUND", "Work item template was not found.");

    private async Task<WorkItemRecurrenceDocument> GetRecurrenceAsync(
        string recurrenceId,
        bool includeArchived,
        CancellationToken ct) =>
        await recurrences.SelectAsync(
            recurrence => recurrence.Id == recurrenceId && (includeArchived || !recurrence.Archived), ct)
        ?? throw new NotFoundException("WORK_ITEM_RECURRENCE_NOT_FOUND", "Work item recurrence was not found.");

    private async Task ReplaceRecurrenceAsync(WorkItemRecurrenceDocument recurrence, CancellationToken ct)
    {
        var result = await recurrences.ReplaceByVersionAsync(
            x => x.Id == recurrence.Id, recurrence, recurrence.Version, ct);
        if (!result.Found)
        {
            throw new ConflictException("WORK_ITEM_RECURRENCE_CONFLICT", "The recurrence changed concurrently; retry the operation.");
        }
        recurrence.Version = result.Version!.Value;
    }

    private async Task<WorkItemRecurrenceResponse> ToResponseAsync(
        WorkItemRecurrenceDocument recurrence,
        CancellationToken ct)
    {
        var generated = await occurrences.CountByFilterAsync(
            occurrence => occurrence.RecurrenceId == recurrence.Id
                && occurrence.Status == WorkItemRecurrenceOccurrenceStates.Generated,
            ct);
        return new WorkItemRecurrenceResponse(
            recurrence.Id,
            recurrence.ProjectId,
            recurrence.TemplateId,
            recurrence.Frequency,
            recurrence.Interval,
            recurrence.StartAtUtc,
            recurrence.EndAtUtc,
            recurrence.NextRunAtUtc,
            recurrence.MaxOccurrences,
            recurrence.ScheduledOccurrences,
            generated,
            recurrence.Active,
            recurrence.Archived,
            recurrence.Version);
    }

    private async Task<ProjectResourceAuthorization> EnsurePermissionAsync(
        string projectId,
        string permission,
        CancellationToken ct) =>
        await permissionChecker.EnsureCanAsync(RequireCurrentUser(), projectId, permission, ct);

    private string RequireCurrentUser() =>
        currentUser.UserId ?? throw new UnauthorizedException("Authenticated user is required.");

    private async Task<IAsyncDisposable> AcquireAsync(string resource, CancellationToken ct)
    {
        var options = lockOptions.Value;
        return await distributedLocks.TryAcquireAsync(
            resource,
            TimeSpan.FromSeconds(Math.Clamp(options.LeaseSeconds, 5, 300)),
            TimeSpan.FromSeconds(Math.Clamp(options.WaitSeconds, 0, 30)),
            ct)
            ?? throw new ConflictException("RESOURCE_BUSY", "The requested resource is busy; retry the operation.");
    }

    private static DateTimeOffset Next(DateTimeOffset value, string frequency, int interval) =>
        frequency switch
        {
            WorkItemRecurrenceFrequencies.Daily => value.AddDays(interval),
            WorkItemRecurrenceFrequencies.Weekly => value.AddDays(checked(interval * 7)),
            WorkItemRecurrenceFrequencies.Monthly => value.AddMonths(interval),
            _ => throw new InvalidOperationException("Stored recurrence frequency is invalid.")
        };

    private static List<string> NormalizeLabels(IReadOnlyCollection<string>? labels)
    {
        var normalized = (labels ?? [])
            .Select(label => Required(label, "Template label", 50))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (normalized.Count > 50)
        {
            throw new ValidationException("A template cannot contain more than 50 labels.");
        }
        return normalized;
    }

    private static string Required(string value, string label, int maximumLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ValidationException(label + " is required.");
        }
        var normalized = value.Trim();
        if (normalized.Length > maximumLength)
        {
            throw new ValidationException($"{label} cannot exceed {maximumLength} characters.");
        }
        return normalized;
    }

    private static string? Optional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static void EnsureOwnership(
        string actualOrganizationId,
        string actualProjectId,
        string expectedOrganizationId,
        string expectedProjectId)
    {
        if (actualOrganizationId != expectedOrganizationId || actualProjectId != expectedProjectId)
        {
            throw new NotFoundException("WORK_ITEM_TEMPLATE_NOT_FOUND", "Work item template was not found.");
        }
    }

    private static WorkItemTemplateResponse ToResponse(WorkItemTemplateDocument template) => new(
        template.Id,
        template.ProjectId,
        template.BoardId,
        template.Name,
        template.Title,
        template.Description,
        template.Type,
        template.Priority,
        template.AssigneeUserId,
        template.TeamId,
        template.DueAfterDays,
        template.Labels,
        template.IssueTypeSchemaVersion,
        template.CustomFields.Select(value => new WorkItemCustomFieldValueResponse(
            value.FieldKey,
            value.Type,
            value.TextValue,
            value.NumberValue,
            value.BooleanValue,
            value.DateValueUtc is null ? null : DateOnly.FromDateTime(value.DateValueUtc.Value.UtcDateTime),
            value.OptionKey)).ToList(),
        template.Archived,
        template.Version);

    private static WorkItemRecurrenceOccurrenceResponse ToResponse(
        WorkItemRecurrenceOccurrenceDocument occurrence) => new(
        occurrence.Id,
        occurrence.ScheduledForUtc,
        occurrence.Status,
        occurrence.CreatedWorkItemId,
        occurrence.GeneratedAt,
        occurrence.Version);

    private sealed record NormalizedTemplate(
        string BoardId,
        string Name,
        string Title,
        string Description,
        string Type,
        int SchemaVersion,
        List<WorkItemCustomFieldValueDocument> CustomFields,
        string Priority,
        string? AssigneeUserId,
        string? TeamId,
        int? DueAfterDays,
        List<string> Labels);

    private sealed record Schedule(
        string Frequency,
        int Interval,
        DateTimeOffset StartAtUtc,
        DateTimeOffset? EndAtUtc,
        int MaxOccurrences);
}

public sealed class RecurringWorkItemGenerator(
    IDocumentRepository<WorkItemTemplateDocument> templates,
    IDocumentRepository<WorkItemRecurrenceDocument> recurrences,
    IDocumentRepository<WorkItemRecurrenceOccurrenceDocument> occurrences,
    IDocumentRepository<WorkItemDocument> workItems,
    IWorkItemTeamPolicy teamPolicy,
    IWorkItemCollaboratorDirectory collaboratorDirectory,
    IBoardPlacementPolicy boardPlacementPolicy,
    IWorkItemTypeSchemaPolicy typeSchemas,
    WorkItemWipProjection wipProjection,
    WorkItemRankService ranks,
    IWorkItemActivityStore activityStore,
    WorkItemCollaborationService collaboration,
    IWorkItemSearchPublisher search,
    IWorkItemAuditPublisher audit,
    IWorkItemNotificationPublisher notifications,
    IWorkItemRealtimePublisher realtime,
    IWorkItemCacheInvalidationPublisher cache,
    IDistributedLockProvider distributedLocks,
    IOptions<DistributedLockOptions> lockOptions,
    IClock clock)
{
    public async Task GenerateAsync(WorkItemRecurrenceDueEvent message, CancellationToken ct)
    {
        var occurrence = await occurrences.SelectAsync(x => x.Id == message.OccurrenceId, ct)
            ?? throw new NotFoundException("WORK_ITEM_RECURRENCE_OCCURRENCE_NOT_FOUND", "Recurrence occurrence was not found.");
        EnsureEventOwnership(occurrence, message);
        if (occurrence.Status == WorkItemRecurrenceOccurrenceStates.Generated)
        {
            return;
        }

        var recurrence = await recurrences.SelectAsync(x => x.Id == message.RecurrenceId, ct)
            ?? throw new NotFoundException("WORK_ITEM_RECURRENCE_NOT_FOUND", "Work item recurrence was not found.");
        if (recurrence.OrganizationId != message.OrganizationId
            || recurrence.ProjectId != message.ProjectId
            || recurrence.TemplateId != occurrence.TemplateId)
        {
            throw new ConflictException("WORK_ITEM_RECURRENCE_OWNERSHIP_INVALID", "Recurrence ownership does not match the durable event.");
        }
        var template = await templates.SelectAsync(x => x.Id == recurrence.TemplateId && !x.Archived, ct)
            ?? throw new NotFoundException("WORK_ITEM_TEMPLATE_NOT_FOUND", "Work item template was not found.");
        if (template.OrganizationId != message.OrganizationId || template.ProjectId != message.ProjectId)
        {
            throw new ConflictException("WORK_ITEM_TEMPLATE_OWNERSHIP_INVALID", "Template ownership does not match the recurrence.");
        }

        var workItemId = occurrence.Id;
        var workItem = await workItems.SelectAsync(x => x.Id == workItemId, ct);
        if (workItem is null)
        {
            workItem = await CreateWorkItemAsync(template, recurrence, occurrence, ct);
        }
        else if (workItem.SourceRecurrenceId != recurrence.Id
                 || workItem.SourceTemplateId != template.Id
                 || workItem.ProjectId != recurrence.ProjectId)
        {
            throw new ConflictException("WORK_ITEM_RECURRENCE_ID_COLLISION", "The deterministic recurrence work item id is already in use.");
        }

        occurrence.Status = WorkItemRecurrenceOccurrenceStates.Generated;
        occurrence.CreatedWorkItemId = workItem.Id;
        occurrence.GeneratedAt ??= clock.UtcNow;
        var result = await occurrences.ReplaceByVersionAsync(
            x => x.Id == occurrence.Id, occurrence, occurrence.Version, ct);
        if (!result.Found)
        {
            throw new ConflictException("WORK_ITEM_RECURRENCE_OCCURRENCE_CONFLICT", "The occurrence changed concurrently; retry processing.");
        }
    }

    private async Task<WorkItemDocument> CreateWorkItemAsync(
        WorkItemTemplateDocument template,
        WorkItemRecurrenceDocument recurrence,
        WorkItemRecurrenceOccurrenceDocument occurrence,
        CancellationToken ct)
    {
        await using var structureLock = await AcquireAsync("project-structure:" + template.ProjectId, ct);
        var shape = await typeSchemas.ValidateAsync(
            template.ProjectId,
            template.Type,
            template.CustomFields.Select(ToRequest).ToList(),
            ct);
        if (template.TeamId is not null)
        {
            await teamPolicy.EnsureCanAssignAsync(template.ProjectId, template.TeamId, template.AssigneeUserId, ct);
        }
        else if (template.AssigneeUserId is not null
                 && !await collaboratorDirectory.IsActiveProjectViewerAsync(
                     template.AssigneeUserId, template.OrganizationId, template.ProjectId, ct))
        {
            throw new ConflictException("WORK_ITEM_TEMPLATE_ASSIGNEE_INELIGIBLE", "Template assignee is no longer eligible for the project.");
        }

        var placement = await boardPlacementPolicy.ResolveInitialAsync(template.ProjectId, template.BoardId, ct);
        var rank = await ranks.NextRankAsync(template.BoardId, placement.ColumnId, null, ct);
        var now = clock.UtcNow;
        var workItem = new WorkItemDocument
        {
            Id = occurrence.Id,
            ProjectId = template.ProjectId,
            BoardId = template.BoardId,
            TeamId = template.TeamId,
            ColumnId = placement.ColumnId,
            Title = template.Title,
            Description = template.Description,
            Type = shape.IssueTypeKey,
            IssueTypeSchemaVersion = shape.SchemaVersion,
            CustomFields = shape.CustomFields.ToList(),
            Priority = template.Priority,
            Status = placement.Status,
            Rank = rank,
            AssigneeUserId = template.AssigneeUserId,
            DueDate = template.DueAfterDays is null
                ? null
                : occurrence.ScheduledForUtc.AddDays(template.DueAfterDays.Value),
            SourceTemplateId = template.Id,
            SourceRecurrenceId = recurrence.Id,
            RecurrenceScheduledForUtc = occurrence.ScheduledForUtc,
            Labels = [.. template.Labels],
            ActivityStorageVersion = 1,
            CreatedAt = now,
            UpdatedAt = now,
            StatusHistory =
            [
                new WorkItemStatusHistoryDocument
                {
                    ToStatus = placement.Status,
                    ChangedByUserId = "system",
                    ChangedAt = now
                }
            ]
        };

        await using (placement.EnforcesWipLimit
            ? await AcquireAsync($"board-column:{template.BoardId}:{placement.ColumnId}", ct)
            : null)
        {
            await wipProjection.ReserveCreateAsync(template.ProjectId, template.BoardId, placement, ct);
            var timeline = workItem.StatusHistory;
            workItem.StatusHistory = [];
            try
            {
                await workItems.CreateAsync(workItem, ct);
            }
            catch (DocumentConflictException)
            {
                var existing = await workItems.SelectAsync(x => x.Id == workItem.Id, ct);
                if (existing is null)
                {
                    throw;
                }
                return existing;
            }
            finally
            {
                workItem.StatusHistory = timeline;
            }
            await activityStore.CreateTimelineAsync(
                WorkItemActivityStore.ToActivity(workItem, template.OrganizationId, timeline[0], 0), ct);
        }

        var correlationId = "recurrence:" + occurrence.Id;
        await search.IndexAsync(WorkItemService.ToSearchRecord(workItem, template.OrganizationId), ct);
        await audit.WriteAsync(
            "RecurringWorkItemGenerated", "WorkItem", workItem.Id, template.Id, occurrence.Id, correlationId, ct);
        await collaboration.RecordActivityAsync(
            workItem,
            template.OrganizationId,
            "RecurringWorkItemGenerated",
            "Generated from recurring template",
            occurrence.Id,
            ct);
        if (workItem.AssigneeUserId is not null)
        {
            await notifications.NotifyAsync(
                workItem.AssigneeUserId,
                "Assignment",
                $"Assigned to {workItem.Title}",
                ct,
                $"recurrence-assignment:{occurrence.Id}:{workItem.AssigneeUserId}");
        }
        await realtime.PublishAsync(new WorkItemRealtimeChange(
            "created",
            workItem.Id,
            workItem.ProjectId,
            workItem.BoardId,
            WorkItemService.ToRealtimeItem(workItem),
            correlationId,
            now,
            WorkItemRealtimeProtocol.CurrentSchemaVersion,
            workItem.Version), ct);
        await cache.InvalidateProjectAsync(workItem.ProjectId, ct);
        return workItem;
    }

    private async Task<IAsyncDisposable> AcquireAsync(string resource, CancellationToken ct)
    {
        var options = lockOptions.Value;
        return await distributedLocks.TryAcquireAsync(
            resource,
            TimeSpan.FromSeconds(Math.Clamp(options.LeaseSeconds, 5, 300)),
            TimeSpan.FromSeconds(Math.Clamp(options.WaitSeconds, 0, 30)),
            ct)
            ?? throw new ConflictException("RESOURCE_BUSY", "The requested resource is busy; retry processing.");
    }

    private static WorkItemCustomFieldValueRequest ToRequest(WorkItemCustomFieldValueDocument value) => new(
        value.FieldKey,
        value.TextValue,
        value.NumberValue,
        value.BooleanValue,
        value.DateValueUtc is null ? null : DateOnly.FromDateTime(value.DateValueUtc.Value.UtcDateTime),
        value.OptionKey);

    private static void EnsureEventOwnership(
        WorkItemRecurrenceOccurrenceDocument occurrence,
        WorkItemRecurrenceDueEvent message)
    {
        if (occurrence.OrganizationId != message.OrganizationId
            || occurrence.ProjectId != message.ProjectId
            || occurrence.RecurrenceId != message.RecurrenceId
            || occurrence.ScheduledForUtc != message.ScheduledForUtc.ToUniversalTime())
        {
            throw new ConflictException("WORK_ITEM_RECURRENCE_EVENT_INVALID", "Durable recurrence event ownership is invalid.");
        }
    }
}
