using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.Modules.Audit;
using Zumbo.Modules.Identity;
using Zumbo.Modules.Notifications;
using Zumbo.Modules.Organizations;
using Zumbo.Modules.Projects;
using Zumbo.Modules.Teams;
using Zumbo.Modules.WorkItems;

namespace Zumbo.Api.Infrastructure.Adapters.Platform.PlatformCore.PrivacyDataProcessorAdapter;

internal sealed class PrivacyDataExportComponent(
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
    int exportLimit)
{
    internal async Task<IReadOnlyCollection<PrivacyDataGroup>> ExportAsync(
        string userId,
        string organizationId,
        CancellationToken ct)
    {
        var organizationData = await PrivacyDocumentAccess.LoadAllAsync(
            organizations,
            x => x.Id == organizationId || x.TenantKey == organizationId,
            ct);
        var teamData = await PrivacyDocumentAccess.LoadAllAsync(
            teams,
            x => x.OrganizationId == organizationId && x.Members.Any(member => member.UserId == userId),
            ct);
        var projectData = await PrivacyDocumentAccess.LoadAllAsync(
            projects,
            x => x.OrganizationId == organizationId,
            ct);
        var projectIds = projectData.Select(x => x.Id).ToHashSet(StringComparer.Ordinal);
        var activityReferences = await workItemActivities.FindUserReferencesAsync(organizationId, userId, ct);
        var activityWorkItemIds = activityReferences.Keys.ToHashSet(StringComparer.Ordinal);
        var workItemData = await PrivacyDocumentAccess.LoadAllAsync(
            workItems,
            x => projectIds.Contains(x.ProjectId)
                && (x.AssigneeUserId == userId
                    || activityWorkItemIds.Contains(x.Id)
                    || x.Comments.Any(comment => comment.AuthorUserId == userId || comment.Mentions.Contains(userId))
                    || x.WorkLogs.Any(log => log.UserId == userId)
                    || x.Approvals.Any(approval => approval.RequestedByUserId == userId || approval.DecidedByUserId == userId)
                    || x.StatusHistory.Any(history => history.ChangedByUserId == userId)),
            ct);
        var notificationData = await PrivacyDocumentAccess.LoadAllAsync(notifications, x => x.UserId == userId, ct);
        var auditData = await PrivacyDocumentAccess.LoadAllAsync(auditLogs, x => x.ActorUserId == userId, ct);
        var collaborationData = await PrivacyDocumentAccess.LoadAllAsync(
            workItemCollaborations,
            x => x.OrganizationId == organizationId
                && (x.WatcherUserIds.Contains(userId) || x.VoterUserIds.Contains(userId)),
            ct);
        var eventActivityData = await PrivacyDocumentAccess.LoadAllAsync(
            workItemEventActivities,
            x => x.OrganizationId == organizationId && x.ActorUserId == userId,
            ct);
        var templateData = await PrivacyDocumentAccess.LoadAllAsync(
            workItemTemplates,
            x => x.OrganizationId == organizationId
                && (x.CreatedByUserId == userId || x.AssigneeUserId == userId),
            ct);
        var recurrenceData = await PrivacyDocumentAccess.LoadAllAsync(
            workItemRecurrences,
            x => x.OrganizationId == organizationId && x.CreatedByUserId == userId,
            ct);
        var bulkJobData = await PrivacyDocumentAccess.LoadAllAsync(
            workItemBulkJobs,
            x => x.OrganizationId == organizationId && x.RequestedByUserId == userId,
            ct);
        var intakeFormData = await PrivacyDocumentAccess.LoadAllAsync(
            intakeForms,
            x => x.OrganizationId == organizationId
                && (x.CreatedByUserId == userId || x.UpdatedByUserId == userId),
            ct);
        var intakeVersionData = await PrivacyDocumentAccess.LoadAllAsync(
            intakeFormVersions,
            x => x.OrganizationId == organizationId && x.PublishedByUserId == userId,
            ct);
        var intakeSubmissionData = await PrivacyDocumentAccess.LoadAllAsync(
            intakeSubmissions,
            x => x.OrganizationId == organizationId
                && (x.SubmittedByUserId == userId || x.TriagedByUserId == userId),
            ct);
        var developmentConnectionData = await PrivacyDocumentAccess.LoadAllAsync(
            developmentConnections,
            x => x.OrganizationId == organizationId
                && x.CreatedByUserId == userId,
            ct);

        return new List<PrivacyDataGroup>
        {
            Group("organizations", organizationData.SelectMany(organization =>
                organization.Departments
                    .Where(department => department.Members.Any(member => member.UserId == userId))
                    .Select(department => new PrivacyDataReference(organization.Id, "department:" + department.Id))), exportLimit),
            Group("teams", teamData.Select(team =>
                new PrivacyDataReference(
                    team.Id,
                    team.Members.First(member => member.UserId == userId).Role)), exportLimit),
            Group("projects", projectData
                .Where(project => project.Members.Any(member => member.UserId == userId))
                .Select(project => new PrivacyDataReference(
                    project.Id,
                    project.Members.First(member => member.UserId == userId).Role)), exportLimit),
            Group("work-items", workItemData.Select(workItem =>
                new PrivacyDataReference(
                    workItem.Id,
                    PrivacyReferenceDescriptions.DescribeWorkItemReference(
                        workItem,
                        userId,
                        activityReferences.GetValueOrDefault(workItem.Id)))), exportLimit),
            Group("work-item-collaboration", collaborationData.Select(item =>
                new PrivacyDataReference(
                    item.WorkItemId,
                    string.Join(',', new[]
                    {
                        item.WatcherUserIds.Contains(userId) ? "watcher" : null,
                        item.VoterUserIds.Contains(userId) ? "voter" : null
                    }.Where(value => value is not null)))), exportLimit),
            Group("work-item-activity", eventActivityData.Select(item =>
                new PrivacyDataReference(item.Id, item.Type)), exportLimit),
            Group("work-item-templates", templateData.Select(item =>
                new PrivacyDataReference(
                    item.Id,
                    item.AssigneeUserId == userId ? "assignee" : "creator")), exportLimit),
            Group("work-item-recurrences", recurrenceData.Select(item =>
                new PrivacyDataReference(item.Id, "creator")), exportLimit),
            Group("work-item-bulk-jobs", bulkJobData.Select(item =>
                new PrivacyDataReference(item.Id, $"{item.Type}:{item.State}")), exportLimit),
            Group("intake-forms", intakeFormData.Select(item =>
                new PrivacyDataReference(item.Id, "author")), exportLimit),
            Group("intake-form-versions", intakeVersionData.Select(item =>
                new PrivacyDataReference(item.Id, "publisher")), exportLimit),
            Group("intake-submissions", intakeSubmissionData.Select(item =>
                new PrivacyDataReference(
                    item.Id,
                    item.SubmittedByUserId == userId ? "submitter" : "triage")), exportLimit),
            Group("development-connections", developmentConnectionData.Select(item =>
                new PrivacyDataReference(item.Id, $"creator:{item.Provider}")), exportLimit),
            Group("notifications", notificationData.Select(notification =>
                new PrivacyDataReference(notification.Id, notification.Type + ":" + notification.Message)), exportLimit),
            Group("audit", auditData.Select(audit =>
                new PrivacyDataReference(audit.Id, $"{audit.Action}:{audit.EntityType}:{audit.EntityId}")), exportLimit)
        };
    }

    internal static PrivacyDataGroup Group(
        string category,
        IEnumerable<PrivacyDataReference> source,
        int exportLimit)
    {
        var items = source.Take(exportLimit + 1).ToList();
        return new PrivacyDataGroup(category, items.Take(exportLimit).ToList(), items.Count > exportLimit);
    }
}
