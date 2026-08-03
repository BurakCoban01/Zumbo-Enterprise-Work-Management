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
}
