using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

public sealed partial class WorkItemTemplateRecurrenceService{

    private async Task<NormalizedTemplate> NormalizeTemplateAsync(
        string projectId,
        string boardId,
        string name,
        string title,
        string? description,
        string type,
        string? priority,
        string? assigneeUserId,
        string? teamId,
        int? dueAfterDays,
        IReadOnlyCollection<string>? labels,
        IReadOnlyCollection<WorkItemCustomFieldValueRequest>? customFields,
        CancellationToken ct)
    {
        var normalizedName = Required(name, "Template name", 120);
        var normalizedTitle = Required(title, "Template title", 200);
        var normalizedDescription = (description ?? string.Empty).Trim();
        if (normalizedDescription.Length > 10_000)
        {
            throw new ValidationException("Template description cannot exceed 10000 characters.");
        }
        if (string.IsNullOrWhiteSpace(boardId))
        {
            throw new ValidationException("Template board is required.");
        }
        if (dueAfterDays is < 0 or > 3_650)
        {
            throw new ValidationException("Template due offset must be between 0 and 3650 days.");
        }

        _ = await boardPlacementPolicy.ResolveInitialAsync(projectId, boardId.Trim(), ct);
        var normalizedTeam = Optional(teamId);
        var normalizedAssignee = Optional(assigneeUserId);
        if (normalizedTeam is not null)
        {
            await teamPolicy.EnsureCanAssignAsync(projectId, normalizedTeam, normalizedAssignee, ct);
        }
        else if (normalizedAssignee is not null)
        {
            var authorization = await permissionChecker.EnsureCanAsync(
                RequireCurrentUser(), projectId, PermissionCatalog.WorkItemView, ct);
            if (!await collaboratorDirectory.IsActiveProjectViewerAsync(
                    normalizedAssignee, authorization.OrganizationId, projectId, ct))
            {
                throw new ValidationException("Template assignee must be an active user who can view the project.");
            }
        }

        var shape = await typeSchemas.ValidateAsync(projectId, type, customFields, ct);
        return new NormalizedTemplate(
            boardId.Trim(),
            normalizedName,
            normalizedTitle,
            normalizedDescription,
            shape.IssueTypeKey,
            shape.SchemaVersion,
            shape.CustomFields.ToList(),
            string.IsNullOrWhiteSpace(priority) ? "Medium" : Required(priority, "Template priority", 50),
            normalizedAssignee,
            normalizedTeam,
            dueAfterDays,
            NormalizeLabels(labels));
    }
}
