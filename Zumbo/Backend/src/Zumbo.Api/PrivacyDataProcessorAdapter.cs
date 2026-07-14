using System.Linq.Expressions;
using Zumbo.BuildingBlocks.Infrastructure.Persistence;
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
    IDocumentRepository<NotificationDocument> notifications,
    IDocumentRepository<NotificationPreferenceDocument> notificationPreferences,
    IDocumentRepository<AuditLogDocument> auditLogs) : IPrivacyDataProcessor
{
    private const int ExportLimit = 5000;

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
        var workItemData = await LoadAllAsync(
            workItems,
            x => projectIds.Contains(x.ProjectId)
                && (x.AssigneeUserId == userId
                    || x.Comments.Any(comment => comment.AuthorUserId == userId || comment.Mentions.Contains(userId))
                    || x.WorkLogs.Any(log => log.UserId == userId)
                    || x.Approvals.Any(approval => approval.RequestedByUserId == userId || approval.DecidedByUserId == userId)
                    || x.StatusHistory.Any(history => history.ChangedByUserId == userId)),
            ct);
        var notificationData = await LoadAllAsync(notifications, x => x.UserId == userId, ct);
        var auditData = await LoadAllAsync(auditLogs, x => x.ActorUserId == userId, ct);

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
                new PrivacyDataReference(workItem.Id, DescribeWorkItemReference(workItem, userId)))),
            Group("notifications", notificationData.Select(notification =>
                new PrivacyDataReference(notification.Id, notification.Type + ":" + notification.Message))),
            Group("audit", auditData.Select(audit =>
                new PrivacyDataReference(audit.Id, $"{audit.Action}:{audit.EntityType}:{audit.EntityId}")))
        };

        return references;
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

        var workItemData = await LoadAllAsync(
            workItems,
            x => projectIds.Contains(x.ProjectId)
                && (x.AssigneeUserId == userId
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

        await notifications.DeleteByFilterAsync(x => x.UserId == userId, ct);
        await notificationPreferences.DeleteByFilterAsync(x => x.UserId == userId, ct);

        var auditData = await LoadAllAsync(auditLogs, x => x.ActorUserId == userId, ct);
        foreach (var audit in auditData)
        {
            audit.ActorUserId = pseudonym;
            audit.OldValue = Scrub(audit.OldValue, username, email);
            audit.NewValue = Scrub(audit.NewValue, username, email);
            audit.IpAddress = null;
            audit.UserAgent = null;
            await auditLogs.ReplaceByFilterAsync(x => x.Id == audit.Id, audit, ct);
        }
    }

    private static PrivacyDataGroup Group(string category, IEnumerable<PrivacyDataReference> source)
    {
        var items = source.Take(ExportLimit + 1).ToList();
        return new PrivacyDataGroup(category, items.Take(ExportLimit).ToList(), items.Count > ExportLimit);
    }

    private static string DescribeWorkItemReference(WorkItemDocument item, string userId)
    {
        var references = new List<string>();
        if (item.AssigneeUserId == userId) references.Add("assignee");
        if (item.Comments.Any(x => x.AuthorUserId == userId)) references.Add("comment-author");
        if (item.Comments.Any(x => x.Mentions.Contains(userId))) references.Add("mention");
        if (item.WorkLogs.Any(x => x.UserId == userId)) references.Add("worklog");
        if (item.Approvals.Any(x => x.RequestedByUserId == userId || x.DecidedByUserId == userId)) references.Add("approval");
        if (item.StatusHistory.Any(x => x.ChangedByUserId == userId)) references.Add("status-history");
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
        for (var page = 1; ; page++)
        {
            var batch = await repository.ListByFilterAsync(
                filter,
                x => x.Id,
                page: page,
                pageSize: 200,
                cancellationToken: ct);
            result.AddRange(batch);
            if (batch.Count < 200)
            {
                return result;
            }
        }
    }
}
