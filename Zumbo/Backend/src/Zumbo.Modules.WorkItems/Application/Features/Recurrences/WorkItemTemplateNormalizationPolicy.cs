using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.Modules.WorkItems;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems.Application.Features.Recurrences;

internal sealed class WorkItemTemplateNormalizationPolicy(
    IWorkItemTeamPolicy teamPolicy,
    IWorkItemCollaboratorDirectory collaboratorDirectory,
    IBoardPlacementPolicy boardPlacementPolicy,
    IWorkItemTypeSchemaPolicy typeSchemas,
    IProjectPermissionChecker permissionChecker,
    ICurrentUser currentUser)
{
    internal async Task<NormalizedWorkItemTemplate> NormalizeAsync(
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
            throw new ValidationException("Template description cannot exceed 10000 characters.");
        if (string.IsNullOrWhiteSpace(boardId))
            throw new ValidationException("Template board is required.");
        if (dueAfterDays is < 0 or > 3_650)
            throw new ValidationException("Template due offset must be between 0 and 3650 days.");

        _ = await boardPlacementPolicy.ResolveInitialAsync(projectId, boardId.Trim(), ct);
        var normalizedTeam = Optional(teamId);
        var normalizedAssignee = Optional(assigneeUserId);
        if (normalizedTeam is not null)
        {
            await teamPolicy.EnsureCanAssignAsync(projectId, normalizedTeam, normalizedAssignee, ct);
        }
        else if (normalizedAssignee is not null)
        {
            var userId = currentUser.UserId
                ?? throw new UnauthorizedException("Authenticated user is required.");
            var authorization = await permissionChecker.EnsureCanAsync(
                userId,
                projectId,
                PermissionCatalog.WorkItemView,
                ct);
            if (!await collaboratorDirectory.IsActiveProjectViewerAsync(
                    normalizedAssignee,
                    authorization.OrganizationId,
                    projectId,
                    ct))
            {
                throw new ValidationException(
                    "Template assignee must be an active user who can view the project.");
            }
        }

        var shape = await typeSchemas.ValidateAsync(projectId, type, customFields, ct);
        return new NormalizedWorkItemTemplate(
            boardId.Trim(),
            normalizedName,
            normalizedTitle,
            normalizedDescription,
            shape.IssueTypeKey,
            shape.SchemaVersion,
            shape.CustomFields.ToList(),
            string.IsNullOrWhiteSpace(priority)
                ? "Medium"
                : Required(priority, "Template priority", 50),
            normalizedAssignee,
            normalizedTeam,
            dueAfterDays,
            NormalizeLabels(labels));
    }

    private static string Required(string value, string label, int maximumLength)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ValidationException(label + " is required.");
        var normalized = value.Trim();
        if (normalized.Length > maximumLength)
            throw new ValidationException($"{label} cannot exceed {maximumLength} characters.");
        return normalized;
    }

    private static string? Optional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static List<string> NormalizeLabels(IReadOnlyCollection<string>? labels)
    {
        var normalized = (labels ?? [])
            .Select(label => Required(label, "Template label", 50))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (normalized.Count > 50)
            throw new ValidationException("A template cannot contain more than 50 labels.");
        return normalized;
    }
}
