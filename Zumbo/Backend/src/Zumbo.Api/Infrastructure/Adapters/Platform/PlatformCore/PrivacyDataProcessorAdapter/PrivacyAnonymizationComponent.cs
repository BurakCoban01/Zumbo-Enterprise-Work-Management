using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.Modules.Identity;
using Zumbo.Modules.Notifications;
using Zumbo.Modules.Organizations;
using Zumbo.Modules.Projects;
using Zumbo.Modules.Teams;
using Zumbo.Modules.WorkItems;

namespace Zumbo.Api.Infrastructure.Adapters.Platform.PlatformCore.PrivacyDataProcessorAdapter;

internal sealed class PrivacyAnonymizationComponent(
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
    IDocumentRepository<NotificationPreferenceDocument> notificationPreferences)
{
    internal async Task AnonymizeReferencesAsync(
        string userId,
        string organizationId,
        string pseudonym,
        string username,
        string email,
        CancellationToken ct)
    {
        var organizationData = await PrivacyDocumentAccess.LoadAllAsync(
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

        var teamData = await PrivacyDocumentAccess.LoadAllAsync(
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

        var projectData = await PrivacyDocumentAccess.LoadAllAsync(projects, x => x.OrganizationId == organizationId, ct);
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

        var collaborationData = await PrivacyDocumentAccess.LoadAllAsync(
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

        var eventActivityData = await PrivacyDocumentAccess.LoadAllAsync(
            workItemEventActivities,
            x => x.OrganizationId == organizationId && x.ActorUserId == userId,
            ct);
        foreach (var activity in eventActivityData)
        {
            activity.ActorUserId = pseudonym;
            activity.Detail = Scrub(activity.Detail, username, email) ?? string.Empty;
            await workItemEventActivities.ReplaceByFilterAsync(x => x.Id == activity.Id, activity, ct);
        }

        var templateData = await PrivacyDocumentAccess.LoadAllAsync(
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

        var recurrenceData = await PrivacyDocumentAccess.LoadAllAsync(
            workItemRecurrences,
            x => x.OrganizationId == organizationId && x.CreatedByUserId == userId,
            ct);
        foreach (var recurrence in recurrenceData)
        {
            recurrence.CreatedByUserId = pseudonym;
            recurrence.UpdatedAt = DateTimeOffset.UtcNow;
            await workItemRecurrences.ReplaceByFilterAsync(x => x.Id == recurrence.Id, recurrence, ct);
        }

        var bulkJobData = await PrivacyDocumentAccess.LoadAllAsync(
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

        var intakeFormData = await PrivacyDocumentAccess.LoadAllAsync(
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

        var intakeVersionData = await PrivacyDocumentAccess.LoadAllAsync(
            intakeFormVersions,
            x => x.OrganizationId == organizationId && x.PublishedByUserId == userId,
            ct);
        foreach (var version in intakeVersionData)
        {
            version.PublishedByUserId = pseudonym;
            await intakeFormVersions.ReplaceByFilterAsync(x => x.Id == version.Id, version, ct);
        }

        var intakeSubmissionData = await PrivacyDocumentAccess.LoadAllAsync(
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

        var developmentConnectionData = await PrivacyDocumentAccess.LoadAllAsync(
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

    internal static string? Scrub(string? value, string username, string email)
    {
        if (value is null) return null;
        return value
            .Replace(username, "[anonymized]", StringComparison.OrdinalIgnoreCase)
            .Replace(email, "[anonymized]", StringComparison.OrdinalIgnoreCase);
    }
}
