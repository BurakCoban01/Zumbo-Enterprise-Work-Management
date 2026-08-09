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

internal sealed class PrivacyStreamExportComponent(
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
    IDocumentRepository<IntakeFormDocument> intakeForms,
    IDocumentRepository<IntakeFormVersionDocument> intakeFormVersions,
    IDocumentRepository<IntakeSubmissionDocument> intakeSubmissions,
    IDocumentRepository<DevelopmentConnectionDocument> developmentConnections,
    IDocumentRepository<NotificationDocument> notifications,
    IDocumentRepository<AuditLogDocument> auditLogs,
    JsonSerializerOptions streamJson)
{
    internal async Task<long> WriteExportAsync(
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
        await PrivacyStreamSerialization.WriteLineAsync(writer, new
        {
            Kind = "profile",
            Category = (string?)null,
            ResourceId = profile.Id,
            Detail = (string?)null,
            Profile = profile
        }, streamJson, ct);
        written++;

        written += await PrivacyDocumentAccess.WriteDocumentsAsync(
            organizations,
            x => x.Id == organizationId || x.TenantKey == organizationId,
            organization => organization.Departments
                .Where(department => department.Members.Any(member => member.UserId == userId))
                .Select(department => new PrivacyDataReference(
                    organization.Id,
                    "department:" + department.Id)),
            reference => PrivacyStreamSerialization.WriteReferenceAsync(writer, "organizations", reference, streamJson, ct),
            ct);
        written += await PrivacyDocumentAccess.WriteDocumentsAsync(
            teams,
            x => x.OrganizationId == organizationId
                && x.Members.Any(member => member.UserId == userId),
            team => team.Members
                .Where(member => member.UserId == userId)
                .Select(member => new PrivacyDataReference(team.Id, member.Role)),
            reference => PrivacyStreamSerialization.WriteReferenceAsync(writer, "teams", reference, streamJson, ct),
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
                    await PrivacyStreamSerialization.WriteReferenceAsync(
                        writer,
                        "projects",
                        new PrivacyDataReference(project.Id, member.Role),
                        streamJson,
                        ct);
                    written++;
                }

                written += await PrivacyDocumentAccess.WriteDocumentsAsync(
                    workItems,
                    x => x.ProjectId == project.Id
                        && (x.AssigneeUserId == userId
                            || x.Comments.Any(comment => comment.AuthorUserId == userId || comment.Mentions.Contains(userId))
                            || x.WorkLogs.Any(log => log.UserId == userId)
                            || x.Approvals.Any(approval => approval.RequestedByUserId == userId || approval.DecidedByUserId == userId)
                            || x.StatusHistory.Any(history => history.ChangedByUserId == userId)),
                    item => [new PrivacyDataReference(item.Id, PrivacyReferenceDescriptions.DescribeWorkItemReference(item, userId, null))],
                    reference => PrivacyStreamSerialization.WriteReferenceAsync(writer, "work-items", reference, streamJson, ct),
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
            await PrivacyStreamSerialization.WriteReferenceAsync(
                writer,
                "work-item-activity-references",
                new PrivacyDataReference(
                    activity.WorkItemId,
                    PrivacyReferenceDescriptions.DescribeActivityReference(activity)),
                streamJson,
                ct);
            written++;
        }

        written += await PrivacyDocumentAccess.WriteDocumentsAsync(
            workItemCollaborations,
            x => x.OrganizationId == organizationId
                && (x.WatcherUserIds.Contains(userId) || x.VoterUserIds.Contains(userId)),
            item => [new PrivacyDataReference(item.WorkItemId, string.Join(',', new[]
            {
                item.WatcherUserIds.Contains(userId) ? "watcher" : null,
                item.VoterUserIds.Contains(userId) ? "voter" : null
            }.Where(value => value is not null)))],
            reference => PrivacyStreamSerialization.WriteReferenceAsync(writer, "work-item-collaboration", reference, streamJson, ct),
            ct);
        written += await PrivacyDocumentAccess.WriteDocumentsAsync(
            workItemEventActivities,
            x => x.OrganizationId == organizationId && x.ActorUserId == userId,
            item => [new PrivacyDataReference(item.Id, item.Type)],
            reference => PrivacyStreamSerialization.WriteReferenceAsync(writer, "work-item-activity", reference, streamJson, ct),
            ct);
        written += await PrivacyDocumentAccess.WriteDocumentsAsync(
            workItemTemplates,
            x => x.OrganizationId == organizationId
                && (x.CreatedByUserId == userId || x.AssigneeUserId == userId),
            item => [new PrivacyDataReference(
                item.Id,
                item.AssigneeUserId == userId ? "assignee" : "creator")],
            reference => PrivacyStreamSerialization.WriteReferenceAsync(writer, "work-item-templates", reference, streamJson, ct),
            ct);
        written += await PrivacyDocumentAccess.WriteDocumentsAsync(
            workItemRecurrences,
            x => x.OrganizationId == organizationId && x.CreatedByUserId == userId,
            item => [new PrivacyDataReference(item.Id, "creator")],
            reference => PrivacyStreamSerialization.WriteReferenceAsync(writer, "work-item-recurrences", reference, streamJson, ct),
            ct);
        written += await PrivacyDocumentAccess.WriteDocumentsAsync(
            workItemBulkJobs,
            x => x.OrganizationId == organizationId && x.RequestedByUserId == userId,
            item => [new PrivacyDataReference(item.Id, $"{item.Type}:{item.State}")],
            reference => PrivacyStreamSerialization.WriteReferenceAsync(writer, "work-item-bulk-jobs", reference, streamJson, ct),
            ct);
        written += await PrivacyDocumentAccess.WriteDocumentsAsync(
            intakeForms,
            x => x.OrganizationId == organizationId
                && (x.CreatedByUserId == userId || x.UpdatedByUserId == userId),
            item => [new PrivacyDataReference(item.Id, "author")],
            reference => PrivacyStreamSerialization.WriteReferenceAsync(writer, "intake-forms", reference, streamJson, ct),
            ct);
        written += await PrivacyDocumentAccess.WriteDocumentsAsync(
            intakeFormVersions,
            x => x.OrganizationId == organizationId && x.PublishedByUserId == userId,
            item => [new PrivacyDataReference(item.Id, "publisher")],
            reference => PrivacyStreamSerialization.WriteReferenceAsync(writer, "intake-form-versions", reference, streamJson, ct),
            ct);
        written += await PrivacyDocumentAccess.WriteDocumentsAsync(
            intakeSubmissions,
            x => x.OrganizationId == organizationId
                && (x.SubmittedByUserId == userId || x.TriagedByUserId == userId),
            item => [new PrivacyDataReference(
                item.Id,
                item.SubmittedByUserId == userId ? "submitter" : "triage")],
            reference => PrivacyStreamSerialization.WriteReferenceAsync(writer, "intake-submissions", reference, streamJson, ct),
            ct);
        written += await PrivacyDocumentAccess.WriteDocumentsAsync(
            developmentConnections,
            x => x.OrganizationId == organizationId
                && x.CreatedByUserId == userId,
            item => [new PrivacyDataReference(item.Id, $"creator:{item.Provider}")],
            reference => PrivacyStreamSerialization.WriteReferenceAsync(writer, "development-connections", reference, streamJson, ct),
            ct);
        written += await PrivacyDocumentAccess.WriteDocumentsAsync(
            notifications,
            x => x.UserId == userId,
            item => [new PrivacyDataReference(item.Id, item.Type + ":" + item.Message)],
            reference => PrivacyStreamSerialization.WriteReferenceAsync(writer, "notifications", reference, streamJson, ct),
            ct);
        written += await PrivacyDocumentAccess.WriteDocumentsAsync(
            auditLogs,
            x => x.OrganizationId == organizationId && x.ActorUserId == userId,
            item => [new PrivacyDataReference(item.Id, $"{item.Action}:{item.EntityType}:{item.EntityId}")],
            reference => PrivacyStreamSerialization.WriteReferenceAsync(writer, "audit", reference, streamJson, ct),
            ct);
        await writer.FlushAsync(ct);
        return written;
    }
}
