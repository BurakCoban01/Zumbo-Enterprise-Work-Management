using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

public sealed partial class WorkItemTypeSchemaService{

    public async Task<WorkItemTypeSchemaResponse> UpsertAsync(
        string projectId,
        UpsertWorkItemTypeSchemaRequest request,
        string correlationId,
        CancellationToken ct)
    {
        await EnsurePermissionAsync(projectId, PermissionCatalog.WorkItemUpdate, ct);
        var candidate = Normalize(projectId, request, clock.UtcNow);
        await using var projectLock = await AcquireProjectLockAsync(projectId, ct);
        var stored = await schemas.SelectAsync(schema => schema.ProjectId == projectId, ct);
        candidate.SchemaVersion = (stored?.SchemaVersion ?? 0) + 1;
        candidate.CreatedAt = stored?.CreatedAt ?? candidate.CreatedAt;
        await ValidateExistingItemsAsync(candidate, ct);

        if (stored is null)
        {
            if (expectedVersions?.ExpectedVersion is long expectedVersion && expectedVersion != 0)
            {
                throw new ConflictException(
                    "WORK_ITEM_SCHEMA_CONCURRENCY_CONFLICT",
                    "Work item type schema changed concurrently; reload and retry.");
            }
            await schemas.CreateAsync(candidate, ct);
        }
        else
        {
            candidate.Id = stored.Id;
            candidate.Version = stored.Version;
            var expectedVersion = expectedVersions?.ExpectedVersion ?? stored.Version;
            var result = await schemas.ReplaceByVersionAsync(
                schema => schema.Id == stored.Id,
                candidate,
                expectedVersion,
                ct);
            if (!result.Found)
            {
                throw new ConflictException(
                    "WORK_ITEM_SCHEMA_CONCURRENCY_CONFLICT",
                    "Work item type schema changed concurrently; reload and retry.");
            }
        }

        await audit.WriteAsync(
            "WorkItemTypeSchemaUpdated",
            "Project",
            projectId,
            stored?.SchemaVersion.ToString(CultureInfo.InvariantCulture),
            candidate.SchemaVersion.ToString(CultureInfo.InvariantCulture),
            correlationId,
            ct);
        return ToResponse(candidate);
    }
}
