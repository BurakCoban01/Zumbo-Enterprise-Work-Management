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

public sealed partial class PrivacyDataProcessorAdapter{

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
}
