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

public sealed partial class PrivacyDataProcessorAdapter(
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

    private readonly PrivacyDataExportComponent dataExport = new(
        organizations,
        teams,
        projects,
        workItems,
        workItemActivities,
        workItemCollaborations,
        workItemEventActivities,
        workItemTemplates,
        workItemRecurrences,
        workItemBulkJobs,
        intakeForms,
        intakeFormVersions,
        intakeSubmissions,
        developmentConnections,
        notifications,
        auditLogs,
        ExportLimit);

    private readonly PrivacyStreamExportComponent streamExport = new(
        organizations,
        teams,
        projects,
        workItems,
        workItemActivities,
        workItemCollaborations,
        workItemEventActivities,
        workItemTemplates,
        workItemRecurrences,
        workItemBulkJobs,
        intakeForms,
        intakeFormVersions,
        intakeSubmissions,
        developmentConnections,
        notifications,
        auditLogs,
        StreamJson);

    private readonly PrivacyAnonymizationComponent anonymization = new(
        organizations,
        teams,
        projects,
        workItems,
        workItemActivities,
        workItemCollaborations,
        workItemEventActivities,
        workItemTemplates,
        workItemRecurrences,
        workItemBulkJobs,
        workItemBulkJobItems,
        workItemBulkArtifacts,
        intakeForms,
        intakeFormVersions,
        intakeSubmissions,
        developmentConnections,
        notifications,
        notificationPreferences);

    private readonly PrivacyAnonymizationGuard anonymizationGuard = new(
        organizations,
        teams,
        projects);
}
