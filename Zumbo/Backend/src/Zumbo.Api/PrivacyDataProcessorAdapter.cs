using System.Linq.Expressions;
using System.Text;
using System.Text.Json;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.Modules.Audit;
using Zumbo.Modules.Identity;
using Zumbo.Modules.Notifications;
using Zumbo.Modules.Organizations;
using Zumbo.Modules.Projects;
using Zumbo.Modules.Teams;
using Zumbo.Modules.WorkItems;
using Zumbo.SharedKernel;

public sealed class PrivacyDataProcessorAdapter(
    IDocumentRepository<OrganizationDocument> organizations,
    IDocumentRepository<TeamDocument> teams,
    IDocumentRepository<ProjectDocument> projects,
    IDocumentRepository<WorkItemDocument> workItems,
    IWorkItemActivityStore workItemActivities,
    IDocumentRepository<WorkItemCollaborationDocument> workItemCollaborations,
    IDocumentRepository<WorkItemEventActivityDocument> workItemEventActivities,
    IDocumentRepository<WorkItemTemplateDocument> workItemTemplates,
    IDocumentRepository<WorkItemRecurrenceDocument> workItemRecurrences,
    IDocumentRepository<WorkItemBulkJobDocument> workItemBulkJobs,
    IDocumentRepository<WorkItemBulkJobItemDocument> workItemBulkJobItems,
    IWorkItemBulkArtifactStorage workItemBulkArtifacts,
    IDocumentRepository<IntakeFormDocument> intakeForms,
    IDocumentRepository<IntakeFormVersionDocument> intakeFormVersions,
    IDocumentRepository<IntakeSubmissionDocument> intakeSubmissions,
    IDocumentRepository<DevelopmentConnectionDocument> developmentConnections,
    IDocumentRepository<NotificationDocument> notifications,
    IDocumentRepository<NotificationPreferenceDocument> notificationPreferences,
    IDocumentRepository<AuditLogDocument> auditLogs) : IPrivacyDataProcessor
{
    private const int ExportLimit = 5000;
    private static readonly JsonSerializerOptions StreamJson = new(JsonSerializerDefaults.Web);

    public async Task<IReadOnlyCollection<PrivacyDataGroup>> ExportAsync(
        string userId,
        string organizationId,
        CancellationToken ct)
    {
        var organizationData = await LoadAllAsync(
            organizations,
            x => x.Id == organizationId || x.TenantKey == organizationId,
            ct);
        var teamData = await LoadAllAsync(
            teams,
            x => x.OrganizationId == organizationId && x.Members.Any(member => member.UserId == userId),
            ct);
        var projectData = await LoadAllAsync(
            projects,
            x => x.OrganizationId == organizationId,
            ct);
        var projectIds = projectData.Select(x => x.Id).ToHashSet(StringComparer.Ordinal);
        var activityReferences = await workItemActivities.FindUserReferencesAsync(organizationId, userId, ct);
        var activityWorkItemIds = activityReferences.Keys.ToHashSet(StringComparer.Ordinal);
        var workItemData = await LoadAllAsync(
            workItems,
            x => projectIds.Contains(x.ProjectId)
                && (x.AssigneeUserId == userId
                    || activityWorkItemIds.Contains(x.Id)
                    || x.Comments.Any(comment => comment.AuthorUserId == userId || comment.Mentions.Contains(userId))
                    || x.WorkLogs.Any(log => log.UserId == userId)
                    || x.Approvals.Any(approval => approval.RequestedByUserId == userId || approval.DecidedByUserId == userId)
                    || x.StatusHistory.Any(history => history.ChangedByUserId == userId)),
            ct);
        var notificationData = await LoadAllAsync(notifications, x => x.UserId == userId, ct);
        var auditData = await LoadAllAsync(auditLogs, x => x.ActorUserId == userId, ct);
        var collaborationData = await LoadAllAsync(
            workItemCollaborations,
            x => x.OrganizationId == organizationId
                && (x.WatcherUserIds.Contains(userId) || x.VoterUserIds.Contains(userId)),
            ct);
        var eventActivityData = await LoadAllAsync(
            workItemEventActivities,
            x => x.OrganizationId == organizationId && x.ActorUserId == userId,
            ct);
        var templateData = await LoadAllAsync(
            workItemTemplates,
            x => x.OrganizationId == organizationId
                && (x.CreatedByUserId == userId || x.AssigneeUserId == userId),
            ct);
        var recurrenceData = await LoadAllAsync(
            workItemRecurrences,
            x => x.OrganizationId == organizationId && x.CreatedByUserId == userId,
            ct);
        var bulkJobData = await LoadAllAsync(
            workItemBulkJobs,
            x => x.OrganizationId == organizationId && x.RequestedByUserId == userId,
            ct);
        var intakeFormData = await LoadAllAsync(
            intakeForms,
            x => x.OrganizationId == organizationId
                && (x.CreatedByUserId == userId || x.UpdatedByUserId == userId),
            ct);
        var intakeVersionData = await LoadAllAsync(
            intakeFormVersions,
            x => x.OrganizationId == organizationId && x.PublishedByUserId == userId,
            ct);
        var intakeSubmissionData = await LoadAllAsync(
            intakeSubmissions,
            x => x.OrganizationId == organizationId
                && (x.SubmittedByUserId == userId || x.TriagedByUserId == userId),
            ct);
        var developmentConnectionData = await LoadAllAsync(
            developmentConnections,
            x => x.OrganizationId == organizationId
                && x.CreatedByUserId == userId,
            ct);

        var references = new List<PrivacyDataGroup>
        {
            Group("organizations", organizationData.SelectMany(organization =>
                organization.Departments
                    .Where(department => department.Members.Any(member => member.UserId == userId))
                    .Select(department => new PrivacyDataReference(organization.Id, "department:" + department.Id)))),
            Group("teams", teamData.Select(team =>
                new PrivacyDataReference(
                    team.Id,
                    team.Members.First(member => member.UserId == userId).Role))),
            Group("projects", projectData
                .Where(project => project.Members.Any(member => member.UserId == userId))
                .Select(project => new PrivacyDataReference(
                    project.Id,
                    project.Members.First(member => member.UserId == userId).Role))),
            Group("work-items", workItemData.Select(workItem =>
                new PrivacyDataReference(
                    workItem.Id,
                    DescribeWorkItemReference(
                        workItem,
                        userId,
                        activityReferences.GetValueOrDefault(workItem.Id))))),
            Group("work-item-collaboration", collaborationData.Select(item =>
                new PrivacyDataReference(
                    item.WorkItemId,
                    string.Join(',', new[]
                    {
                        item.WatcherUserIds.Contains(userId) ? "watcher" : null,
                        item.VoterUserIds.Contains(userId) ? "voter" : null
                    }.Where(value => value is not null))))),
            Group("work-item-activity", eventActivityData.Select(item =>
                new PrivacyDataReference(item.Id, item.Type))),
            Group("work-item-templates", templateData.Select(item =>
                new PrivacyDataReference(
                    item.Id,
                    item.AssigneeUserId == userId ? "assignee" : "creator"))),
            Group("work-item-recurrences", recurrenceData.Select(item =>
                new PrivacyDataReference(item.Id, "creator"))),
            Group("work-item-bulk-jobs", bulkJobData.Select(item =>
                new PrivacyDataReference(item.Id, $"{item.Type}:{item.State}"))),
            Group("intake-forms", intakeFormData.Select(item =>
                new PrivacyDataReference(item.Id, "author"))),
            Group("intake-form-versions", intakeVersionData.Select(item =>
                new PrivacyDataReference(item.Id, "publisher"))),
            Group("intake-submissions", intakeSubmissionData.Select(item =>
                new PrivacyDataReference(
                    item.Id,
                    item.SubmittedByUserId == userId ? "submitter" : "triage"))),
            Group("development-connections", developmentConnectionData.Select(item =>
                new PrivacyDataReference(item.Id, $"creator:{item.Provider}"))),
            Group("notifications", notificationData.Select(notification =>
                new PrivacyDataReference(notification.Id, notification.Type + ":" + notification.Message))),
            Group("audit", auditData.Select(audit =>
                new PrivacyDataReference(audit.Id, $"{audit.Action}:{audit.EntityType}:{audit.EntityId}")))
        };

        return references;
    }

    public async Task<long> WriteExportAsync(
        string userId,
        string organizationId,
        UserProfileResponse profile,
        Stream destination,
        CancellationToken ct)
    {
        await using var writer = new StreamWriter(
            destination,
            new UTF8Encoding(false),
            16 * 1024,
            leaveOpen: true);
        long written = 0;
        await WriteLineAsync(writer, new PrivacyStreamLine(
            "profile", null, profile.Id, null, profile), ct);
        written++;

        written += await WriteDocumentsAsync(
            organizations,
            x => x.Id == organizationId || x.TenantKey == organizationId,
            "organizations",
            organization => organization.Departments
                .Where(department => department.Members.Any(member => member.UserId == userId))
                .Select(department => new PrivacyDataReference(
                    organization.Id,
                    "department:" + department.Id)),
            writer,
            ct);
        written += await WriteDocumentsAsync(
            teams,
            x => x.OrganizationId == organizationId
                && x.Members.Any(member => member.UserId == userId),
            "teams",
            team => team.Members
                .Where(member => member.UserId == userId)
                .Select(member => new PrivacyDataReference(team.Id, member.Role)),
            writer,
            ct);

        string? projectCursor = null;
        do
        {
            var projectPage = await projects.ListByCursorAsync(
                x => x.OrganizationId == organizationId,
                projectCursor,
                200,
                ct);
            foreach (var project in projectPage.Items)
            {
                foreach (var member in project.Members.Where(member => member.UserId == userId))
                {
                    await WriteReferenceAsync(
                        writer,
                        "projects",
                        new PrivacyDataReference(project.Id, member.Role),
                        ct);
                    written++;
                }

                written += await WriteDocumentsAsync(
                    workItems,
                    x => x.ProjectId == project.Id
                        && (x.AssigneeUserId == userId
                            || x.Comments.Any(comment => comment.AuthorUserId == userId || comment.Mentions.Contains(userId))
                            || x.WorkLogs.Any(log => log.UserId == userId)
                            || x.Approvals.Any(approval => approval.RequestedByUserId == userId || approval.DecidedByUserId == userId)
                            || x.StatusHistory.Any(history => history.ChangedByUserId == userId)),
                    "work-items",
                    item => [new PrivacyDataReference(item.Id, DescribeWorkItemReference(item, userId, null))],
                    writer,
                    ct);
            }
            projectCursor = projectPage.NextCursor;
        }
        while (projectCursor is not null);

        await foreach (var activity in workItemActivities.StreamUserReferencesAsync(
            organizationId,
            userId,
            ct))
        {
            await WriteReferenceAsync(
                writer,
                "work-item-activity-references",
                new PrivacyDataReference(
                    activity.WorkItemId,
                    DescribeActivityReference(activity)),
                ct);
            written++;
        }

        written += await WriteDocumentsAsync(
            workItemCollaborations,
            x => x.OrganizationId == organizationId
                && (x.WatcherUserIds.Contains(userId) || x.VoterUserIds.Contains(userId)),
            "work-item-collaboration",
            item => [new PrivacyDataReference(item.WorkItemId, string.Join(',', new[]
            {
                item.WatcherUserIds.Contains(userId) ? "watcher" : null,
                item.VoterUserIds.Contains(userId) ? "voter" : null
            }.Where(value => value is not null)))],
            writer,
            ct);
        written += await WriteDocumentsAsync(
            workItemEventActivities,
            x => x.OrganizationId == organizationId && x.ActorUserId == userId,
            "work-item-activity",
            item => [new PrivacyDataReference(item.Id, item.Type)],
            writer,
            ct);
        written += await WriteDocumentsAsync(
            workItemTemplates,
            x => x.OrganizationId == organizationId
                && (x.CreatedByUserId == userId || x.AssigneeUserId == userId),
            "work-item-templates",
            item => [new PrivacyDataReference(
                item.Id,
                item.AssigneeUserId == userId ? "assignee" : "creator")],
            writer,
            ct);
        written += await WriteDocumentsAsync(
            workItemRecurrences,
            x => x.OrganizationId == organizationId && x.CreatedByUserId == userId,
            "work-item-recurrences",
            item => [new PrivacyDataReference(item.Id, "creator")],
            writer,
            ct);
        written += await WriteDocumentsAsync(
            workItemBulkJobs,
            x => x.OrganizationId == organizationId && x.RequestedByUserId == userId,
            "work-item-bulk-jobs",
            item => [new PrivacyDataReference(item.Id, $"{item.Type}:{item.State}")],
            writer,
            ct);
        written += await WriteDocumentsAsync(
            intakeForms,
            x => x.OrganizationId == organizationId
                && (x.CreatedByUserId == userId || x.UpdatedByUserId == userId),
            "intake-forms",
            item => [new PrivacyDataReference(item.Id, "author")],
            writer,
            ct);
        written += await WriteDocumentsAsync(
            intakeFormVersions,
            x => x.OrganizationId == organizationId && x.PublishedByUserId == userId,
            "intake-form-versions",
            item => [new PrivacyDataReference(item.Id, "publisher")],
            writer,
            ct);
        written += await WriteDocumentsAsync(
            intakeSubmissions,
            x => x.OrganizationId == organizationId
                && (x.SubmittedByUserId == userId || x.TriagedByUserId == userId),
            "intake-submissions",
            item => [new PrivacyDataReference(
                item.Id,
                item.SubmittedByUserId == userId ? "submitter" : "triage")],
            writer,
            ct);
        written += await WriteDocumentsAsync(
            developmentConnections,
            x => x.OrganizationId == organizationId
                && x.CreatedByUserId == userId,
            "development-connections",
            item => [new PrivacyDataReference(item.Id, $"creator:{item.Provider}")],
            writer,
            ct);
        written += await WriteDocumentsAsync(
            notifications,
            x => x.UserId == userId,
            "notifications",
            item => [new PrivacyDataReference(item.Id, item.Type + ":" + item.Message)],
            writer,
            ct);
        written += await WriteDocumentsAsync(
            auditLogs,
            x => x.OrganizationId == organizationId && x.ActorUserId == userId,
            "audit",
            item => [new PrivacyDataReference(item.Id, $"{item.Action}:{item.EntityType}:{item.EntityId}")],
            writer,
            ct);
        await writer.FlushAsync(ct);
        return written;
    }

    public async Task EnsureCanAnonymizeAsync(string userId, string organizationId, CancellationToken ct)
    {
        var ownedOrganization = await organizations.SelectAsync(
            x => (x.Id == organizationId || x.TenantKey == organizationId) && x.OwnerUserId == userId,
            ct);
        if (ownedOrganization is not null)
        {
            throw new ConflictException(
                "PRIVACY_OWNERSHIP_TRANSFER_REQUIRED",
                "Organization ownership must be transferred before anonymization.");
        }

        var ownedTeam = await teams.SelectAsync(
            x => x.OrganizationId == organizationId
                && !x.Archived
                && x.Members.Any(member => member.UserId == userId && member.Status == "Active" && member.Role == "Owner"),
            ct);
        var ownedProject = await projects.SelectAsync(
            x => x.OrganizationId == organizationId
                && !x.Archived
                && x.Members.Any(member => member.UserId == userId && member.Role == "ProjectOwner"),
            ct);
        if (ownedTeam is not null || ownedProject is not null)
        {
            throw new ConflictException(
                "PRIVACY_OWNERSHIP_TRANSFER_REQUIRED",
                "Team and project ownership must be transferred before anonymization.");
        }
    }

    public async Task AnonymizeReferencesAsync(
        string userId,
        string organizationId,
        string pseudonym,
        string username,
        string email,
        CancellationToken ct)
    {
        var organizationData = await LoadAllAsync(
            organizations,
            x => x.Id == organizationId || x.TenantKey == organizationId,
            ct);
        foreach (var organization in organizationData)
        {
            foreach (var department in organization.Departments)
            {
                department.Members.RemoveAll(member => member.UserId == userId);
            }
            await organizations.ReplaceByFilterAsync(x => x.Id == organization.Id, organization, ct);
        }

        var teamData = await LoadAllAsync(
            teams,
            x => x.OrganizationId == organizationId && x.Members.Any(member => member.UserId == userId),
            ct);
        foreach (var team in teamData)
        {
            if (team.Archived)
            {
                foreach (var member in team.Members.Where(member => member.UserId == userId))
                {
                    member.UserId = pseudonym;
                    member.Email = pseudonym + "@invalid.local";
                    member.Status = "Removed";
                }
            }
            else
            {
                team.Members.RemoveAll(member => member.UserId == userId);
            }
            await teams.ReplaceByFilterAsync(x => x.Id == team.Id, team, ct);
        }

        var projectData = await LoadAllAsync(projects, x => x.OrganizationId == organizationId, ct);
        var projectIds = projectData.Select(x => x.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var project in projectData.Where(project => project.Members.Any(member => member.UserId == userId)))
        {
            if (project.Archived)
            {
                foreach (var member in project.Members.Where(member => member.UserId == userId))
                {
                    member.UserId = pseudonym;
                }
            }
            else
            {
                project.Members.RemoveAll(member => member.UserId == userId);
            }
            await projects.ReplaceByFilterAsync(x => x.Id == project.Id, project, ct);
        }

        var activityReferences = await workItemActivities.FindUserReferencesAsync(organizationId, userId, ct);
        var activityWorkItemIds = activityReferences.Keys.ToHashSet(StringComparer.Ordinal);
        var workItemData = await LoadAllAsync(
            workItems,
            x => projectIds.Contains(x.ProjectId)
                && (x.AssigneeUserId == userId
                    || activityWorkItemIds.Contains(x.Id)
                    || x.Comments.Any(comment => comment.AuthorUserId == userId || comment.Mentions.Contains(userId))
                    || x.WorkLogs.Any(log => log.UserId == userId)
                    || x.Approvals.Any(approval => approval.RequestedByUserId == userId || approval.DecidedByUserId == userId)
                    || x.StatusHistory.Any(history => history.ChangedByUserId == userId)),
            ct);
        foreach (var workItem in workItemData)
        {
            if (workItem.AssigneeUserId == userId)
            {
                workItem.AssigneeUserId = null;
            }
            foreach (var comment in workItem.Comments)
            {
                if (comment.AuthorUserId == userId) comment.AuthorUserId = pseudonym;
                comment.Mentions.RemoveAll(mention => mention == userId);
                foreach (var revision in comment.History.Where(revision => revision.EditedByUserId == userId))
                {
                    revision.EditedByUserId = pseudonym;
                }
            }
            foreach (var log in workItem.WorkLogs.Where(log => log.UserId == userId)) log.UserId = pseudonym;
            foreach (var approval in workItem.Approvals)
            {
                if (approval.RequestedByUserId == userId) approval.RequestedByUserId = pseudonym;
                if (approval.DecidedByUserId == userId) approval.DecidedByUserId = pseudonym;
            }
            foreach (var history in workItem.StatusHistory.Where(history => history.ChangedByUserId == userId))
            {
                history.ChangedByUserId = pseudonym;
            }
            await workItems.ReplaceByFilterAsync(x => x.Id == workItem.Id, workItem, ct);
        }
        await workItemActivities.AnonymizeUserReferencesAsync(organizationId, userId, pseudonym, ct);

        var collaborationData = await LoadAllAsync(
            workItemCollaborations,
            x => x.OrganizationId == organizationId
                && (x.WatcherUserIds.Contains(userId) || x.VoterUserIds.Contains(userId)),
            ct);
        foreach (var collaboration in collaborationData)
        {
            collaboration.WatcherUserIds.RemoveAll(id => id == userId);
            collaboration.VoterUserIds.RemoveAll(id => id == userId);
            collaboration.UpdatedAt = DateTimeOffset.UtcNow;
            await workItemCollaborations.ReplaceByFilterAsync(x => x.Id == collaboration.Id, collaboration, ct);
        }

        var eventActivityData = await LoadAllAsync(
            workItemEventActivities,
            x => x.OrganizationId == organizationId && x.ActorUserId == userId,
            ct);
        foreach (var activity in eventActivityData)
        {
            activity.ActorUserId = pseudonym;
            activity.Detail = Scrub(activity.Detail, username, email) ?? string.Empty;
            await workItemEventActivities.ReplaceByFilterAsync(x => x.Id == activity.Id, activity, ct);
        }

        var templateData = await LoadAllAsync(
            workItemTemplates,
            x => x.OrganizationId == organizationId
                && (x.CreatedByUserId == userId || x.AssigneeUserId == userId),
            ct);
        foreach (var template in templateData)
        {
            if (template.CreatedByUserId == userId) template.CreatedByUserId = pseudonym;
            if (template.AssigneeUserId == userId) template.AssigneeUserId = null;
            template.UpdatedAt = DateTimeOffset.UtcNow;
            await workItemTemplates.ReplaceByFilterAsync(x => x.Id == template.Id, template, ct);
        }

        var recurrenceData = await LoadAllAsync(
            workItemRecurrences,
            x => x.OrganizationId == organizationId && x.CreatedByUserId == userId,
            ct);
        foreach (var recurrence in recurrenceData)
        {
            recurrence.CreatedByUserId = pseudonym;
            recurrence.UpdatedAt = DateTimeOffset.UtcNow;
            await workItemRecurrences.ReplaceByFilterAsync(x => x.Id == recurrence.Id, recurrence, ct);
        }

        var bulkJobData = await LoadAllAsync(
            workItemBulkJobs,
            x => x.OrganizationId == organizationId && x.RequestedByUserId == userId,
            ct);
        foreach (var job in bulkJobData)
        {
            if (!string.IsNullOrWhiteSpace(job.ResultStoragePath))
                await workItemBulkArtifacts.DeleteAsync(job.ResultStoragePath, ct);
            if (!string.IsNullOrWhiteSpace(job.ErrorStoragePath))
                await workItemBulkArtifacts.DeleteAsync(job.ErrorStoragePath, ct);
            await workItemBulkJobItems.DeleteByFilterAsync(x => x.JobId == job.Id, ct);
            await workItemBulkJobs.DeleteByFilterAsync(x => x.Id == job.Id, ct);
        }

        var intakeFormData = await LoadAllAsync(
            intakeForms,
            x => x.OrganizationId == organizationId
                && (x.CreatedByUserId == userId || x.UpdatedByUserId == userId),
            ct);
        foreach (var form in intakeFormData)
        {
            if (form.CreatedByUserId == userId) form.CreatedByUserId = pseudonym;
            if (form.UpdatedByUserId == userId) form.UpdatedByUserId = pseudonym;
            await intakeForms.ReplaceByFilterAsync(x => x.Id == form.Id, form, ct);
        }

        var intakeVersionData = await LoadAllAsync(
            intakeFormVersions,
            x => x.OrganizationId == organizationId && x.PublishedByUserId == userId,
            ct);
        foreach (var version in intakeVersionData)
        {
            version.PublishedByUserId = pseudonym;
            await intakeFormVersions.ReplaceByFilterAsync(x => x.Id == version.Id, version, ct);
        }

        var intakeSubmissionData = await LoadAllAsync(
            intakeSubmissions,
            x => x.OrganizationId == organizationId
                && (x.SubmittedByUserId == userId || x.TriagedByUserId == userId),
            ct);
        foreach (var submission in intakeSubmissionData)
        {
            if (submission.SubmittedByUserId == userId) submission.SubmittedByUserId = pseudonym;
            if (submission.TriagedByUserId == userId) submission.TriagedByUserId = pseudonym;
            submission.TriageNote = Scrub(submission.TriageNote, username, email);
            foreach (var value in submission.Values)
            {
                value.Value = Scrub(value.Value, username, email) ?? string.Empty;
            }
            foreach (var attachment in submission.Attachments)
            {
                attachment.FileName = Scrub(attachment.FileName, username, email) ?? "attachment";
            }
            await intakeSubmissions.ReplaceByFilterAsync(x => x.Id == submission.Id, submission, ct);
        }

        var developmentConnectionData = await LoadAllAsync(
            developmentConnections,
            x => x.OrganizationId == organizationId
                && x.CreatedByUserId == userId,
            ct);
        foreach (var connection in developmentConnectionData)
        {
            connection.CreatedByUserId = pseudonym;
            connection.UpdatedAtUtc = DateTimeOffset.UtcNow;
            await developmentConnections.ReplaceByFilterAsync(
                x => x.Id == connection.Id,
                connection,
                ct);
        }

        await notifications.DeleteByFilterAsync(x => x.UserId == userId, ct);
        await notificationPreferences.DeleteByFilterAsync(x => x.UserId == userId, ct);

        // Audit records are immutable compliance evidence. Their lifecycle is governed by
        // the tenant retention policy instead of in-place privacy mutation.
    }

    private static async Task<long> WriteDocumentsAsync<TDocument>(
        IDocumentRepository<TDocument> repository,
        Expression<Func<TDocument, bool>> filter,
        string category,
        Func<TDocument, IEnumerable<PrivacyDataReference>> select,
        StreamWriter writer,
        CancellationToken ct)
        where TDocument : class, IDocument
    {
        long written = 0;
        string? cursor = null;
        do
        {
            var page = await repository.ListByCursorAsync(filter, cursor, 200, ct);
            foreach (var document in page.Items)
            {
                foreach (var reference in select(document))
                {
                    await WriteReferenceAsync(writer, category, reference, ct);
                    written++;
                }
            }
            cursor = page.NextCursor;
        }
        while (cursor is not null);
        return written;
    }

    private static Task WriteReferenceAsync(
        StreamWriter writer,
        string category,
        PrivacyDataReference reference,
        CancellationToken ct) =>
        WriteLineAsync(writer, new PrivacyStreamLine(
            "reference",
            category,
            reference.ResourceId,
            reference.Detail,
            null), ct);

    private static Task WriteLineAsync(
        StreamWriter writer,
        PrivacyStreamLine line,
        CancellationToken ct) =>
        writer.WriteLineAsync(JsonSerializer.Serialize(line, StreamJson).AsMemory(), ct);

    private static string DescribeActivityReference(WorkItemUserActivityReference activity) =>
        string.Join(',', new[]
        {
            activity.CommentAuthor ? "comment-author" : null,
            activity.CommentRevision ? "comment-revision" : null,
            activity.Mention ? "mention" : null,
            activity.WorkLog ? "worklog" : null,
            activity.Approval ? "approval" : null,
            activity.Timeline ? "status-history" : null
        }.Where(value => value is not null));

    private static PrivacyDataGroup Group(string category, IEnumerable<PrivacyDataReference> source)
    {
        var items = source.Take(ExportLimit + 1).ToList();
        return new PrivacyDataGroup(category, items.Take(ExportLimit).ToList(), items.Count > ExportLimit);
    }

    private static string DescribeWorkItemReference(
        WorkItemDocument item,
        string userId,
        WorkItemUserActivityReference? activity)
    {
        var references = new List<string>();
        if (item.AssigneeUserId == userId) references.Add("assignee");
        if (activity?.CommentAuthor == true || item.Comments.Any(x => x.AuthorUserId == userId)) references.Add("comment-author");
        if (activity?.CommentRevision == true || item.Comments.Any(x => x.History.Any(r => r.EditedByUserId == userId))) references.Add("comment-revision");
        if (activity?.Mention == true || item.Comments.Any(x => x.Mentions.Contains(userId))) references.Add("mention");
        if (activity?.WorkLog == true || item.WorkLogs.Any(x => x.UserId == userId)) references.Add("worklog");
        if (activity?.Approval == true || item.Approvals.Any(x => x.RequestedByUserId == userId || x.DecidedByUserId == userId)) references.Add("approval");
        if (activity?.Timeline == true || item.StatusHistory.Any(x => x.ChangedByUserId == userId)) references.Add("status-history");
        return string.Join(',', references);
    }

    private static string? Scrub(string? value, string username, string email)
    {
        if (value is null) return null;
        return value
            .Replace(username, "[anonymized]", StringComparison.OrdinalIgnoreCase)
            .Replace(email, "[anonymized]", StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<IReadOnlyList<TDocument>> LoadAllAsync<TDocument>(
        IDocumentRepository<TDocument> repository,
        Expression<Func<TDocument, bool>> filter,
        CancellationToken ct)
        where TDocument : class, IDocument
    {
        var result = new List<TDocument>();
        string? cursor = null;
        do
        {
            var page = await repository.ListByCursorAsync(
                filter,
                cursor,
                pageSize: 200,
                cancellationToken: ct);
            result.AddRange(page.Items);
            cursor = page.NextCursor;
        }
        while (cursor is not null);

        return result;
    }

    private sealed record PrivacyStreamLine(
        string Kind,
        string? Category,
        string ResourceId,
        string? Detail,
        UserProfileResponse? Profile);
}
