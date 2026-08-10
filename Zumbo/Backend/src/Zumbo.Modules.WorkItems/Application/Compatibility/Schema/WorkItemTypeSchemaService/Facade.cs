using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.Modules.WorkItems.Application.Features.Schema;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

public sealed partial class WorkItemTypeSchemaService
{
public async Task<WorkItemTypeSchemaResponse> GetAsync(string projectId, CancellationToken ct) =>
        await getWorkItemTypeSchemaHandler.HandleAsync(new GetWorkItemTypeSchemaQuery(projectId), ct);

public async Task<WorkItemFieldDistributionResponse> GetCustomFieldDistributionAsync(
        string projectId,
        string fieldKey,
        CancellationToken ct)
        => await getCustomFieldDistributionHandler.HandleAsync(
            new GetCustomFieldDistributionQuery(projectId, fieldKey),
            ct);

public async Task<WorkItemFieldDistributionResponse> GetIssueTypeDistributionAsync(
        string projectId,
        CancellationToken ct)
        => await getIssueTypeDistributionHandler.HandleAsync(
            new GetIssueTypeDistributionQuery(projectId),
            ct);

public async Task<string> HierarchyLevelAsync(
        string projectId,
        string issueTypeKey,
        CancellationToken ct) =>
        await getIssueTypeHierarchyHandler.HandleAsync(
            new GetIssueTypeHierarchyQuery(projectId, issueTypeKey),
            ct);

public async Task<WorkItemTypeSchemaResponse> UpsertAsync(
        string projectId,
        UpsertWorkItemTypeSchemaRequest request,
        string correlationId,
        CancellationToken ct)
        => await upsertWorkItemTypeSchemaHandler.HandleAsync(
            new UpsertWorkItemTypeSchemaCommand(projectId, request, correlationId),
            ct);

public async Task<ValidatedWorkItemShape> ValidateAsync(
        string projectId,
        string issueTypeKey,
        IReadOnlyCollection<WorkItemCustomFieldValueRequest>? values,
        CancellationToken ct)
        => await validateWorkItemShapeHandler.HandleAsync(
            new ValidateWorkItemShapeQuery(projectId, issueTypeKey, values),
            ct);

public async Task<ValidatedWorkItemSearchFilter> ValidateSearchFilterAsync(
        string projectId,
        string? issueTypeKey,
        string? customFieldKey,
        string? customFieldValue,
        CancellationToken ct)
        => await validateWorkItemSearchFilterHandler.HandleAsync(
            new ValidateWorkItemSearchFilterQuery(
                projectId,
                issueTypeKey,
                customFieldKey,
                customFieldValue),
            ct);
}
