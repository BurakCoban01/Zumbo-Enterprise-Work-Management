using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.Modules.WorkItems.Application.Features.Recurrences;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

public sealed partial class WorkItemTemplateRecurrenceService(
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
    private readonly ListWorkItemTemplatesHandler listWorkItemTemplatesHandler =
        new(templates, permissionChecker, currentUser);
    private readonly ListWorkItemRecurrencesHandler listWorkItemRecurrencesHandler =
        new(recurrences, occurrences, permissionChecker, currentUser);
    private readonly ListRecurrenceOccurrencesHandler listRecurrenceOccurrencesHandler =
        new(recurrences, occurrences, permissionChecker, currentUser);
    private readonly PreviewWorkItemRecurrenceHandler previewWorkItemRecurrenceHandler =
        new(templates, permissionChecker, currentUser, configuredOptions, clock);
    private readonly CreateWorkItemRecurrenceHandler createWorkItemRecurrenceHandler =
        new(templates, recurrences, occurrences, permissionChecker, currentUser, configuredOptions, clock, audit);
    private readonly SetWorkItemRecurrenceStateHandler setWorkItemRecurrenceStateHandler =
        new(recurrences, occurrences, permissionChecker, currentUser, distributedLocks, lockOptions, clock, audit, expectedVersions);
    private readonly ArchiveWorkItemRecurrenceHandler archiveWorkItemRecurrenceHandler =
        new(recurrences, permissionChecker, currentUser, distributedLocks, lockOptions, clock, audit, expectedVersions);
    private readonly CreateWorkItemTemplateHandler createWorkItemTemplateHandler =
        new(templates, permissionChecker, currentUser, teamPolicy, collaboratorDirectory, boardPlacementPolicy, typeSchemas, distributedLocks, lockOptions, clock, audit);
    private readonly UpdateWorkItemTemplateHandler updateWorkItemTemplateHandler =
        new(templates, recurrences, permissionChecker, currentUser, teamPolicy, collaboratorDirectory, boardPlacementPolicy, typeSchemas, distributedLocks, lockOptions, clock, audit, expectedVersions);
    private readonly ArchiveWorkItemTemplateHandler archiveWorkItemTemplateHandler =
        new(templates, recurrences, permissionChecker, currentUser, distributedLocks, lockOptions, clock, audit, expectedVersions);
    private readonly ScheduleDueRecurrencesHandler scheduleDueRecurrencesHandler =
        new(templates, recurrences, occurrences, recurrencePublisher, distributedLocks, lockOptions, configuredOptions, clock);
    private WorkItemRecurrenceOptions Options => configuredOptions.Value;
}
