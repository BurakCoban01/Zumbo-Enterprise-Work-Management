using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.Modules.WorkItems.Application.Features.Schema;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

public sealed partial class WorkItemTypeSchemaService(
    IDocumentRepository<WorkItemTypeSchemaDocument> schemas,
    IDocumentRepository<WorkItemDocument> workItems,
    IProjectPermissionChecker permissionChecker,
    IWorkItemAuditPublisher audit,
    IDistributedLockProvider distributedLocks,
    IOptions<DistributedLockOptions> lockOptions,
    IOptions<WorkItemTypeSchemaOptions> configuredOptions,
    IClock clock,
    ICurrentUser currentUser,
    IExpectedVersionAccessor? expectedVersions = null) : IWorkItemTypeSchemaPolicy
{
    private int BatchSize => Math.Clamp(configuredOptions.Value.BatchSize, 1, 200);
    private int MaxBatches => Math.Clamp(configuredOptions.Value.MaxBatchesPerValidation, 1, 10_000);
    private readonly GetWorkItemTypeSchemaHandler getWorkItemTypeSchemaHandler =
        new(schemas, workItems, permissionChecker, currentUser, configuredOptions, clock);
    private readonly GetIssueTypeDistributionHandler getIssueTypeDistributionHandler =
        new(schemas, workItems, permissionChecker, currentUser, configuredOptions, clock);
    private readonly GetCustomFieldDistributionHandler getCustomFieldDistributionHandler =
        new(schemas, workItems, permissionChecker, currentUser, configuredOptions, clock);
    private readonly UpsertWorkItemTypeSchemaHandler upsertWorkItemTypeSchemaHandler =
        new(schemas, workItems, permissionChecker, audit, distributedLocks, lockOptions,
            configuredOptions, clock, currentUser, expectedVersions);
    private readonly ValidateWorkItemShapeHandler validateWorkItemShapeHandler = new(schemas, clock);
    private readonly GetIssueTypeHierarchyHandler getIssueTypeHierarchyHandler = new(schemas, clock);
    private readonly ValidateWorkItemSearchFilterHandler validateWorkItemSearchFilterHandler =
        new(schemas, clock);
}
