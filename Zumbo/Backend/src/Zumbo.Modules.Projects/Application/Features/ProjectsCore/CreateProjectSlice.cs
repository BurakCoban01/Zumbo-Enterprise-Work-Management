using System.Text.RegularExpressions;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Projects;

internal sealed partial class CreateProjectSlice(
    IDocumentRepository<ProjectDocument> projects,
    IProjectMemberDirectory memberDirectory,
    IProjectOrganizationDirectory organizationDirectory,
    IProjectAuditWriter audit,
    IClock clock,
    ICurrentUser currentUser)
{
    internal async Task<ProjectResponse> HandleAsync(
        CreateProjectRequest request,
        string correlationId,
        CancellationToken ct)
    {
        CreateProjectValidator.Validate(request);
        var organizationId = request.OrganizationId.Trim();
        EnsureOrganizationScope(organizationId);
        await organizationDirectory.EnsureActiveAsync(organizationId, ct);
        var userId = CurrentUserId();
        if (!IsSystemAdmin() && !string.Equals(request.OwnerUserId, userId, StringComparison.Ordinal))
        {
            throw new ForbiddenException("A project can only be created for the authenticated owner.");
        }

        var ownerUserId = request.OwnerUserId.Trim();
        await memberDirectory.EnsureEligibleAsync(ownerUserId, organizationId, ct);
        var key = NormalizeKey(request.Key);
        if (await projects.ExistsByFilterAsync(
            candidate => candidate.OrganizationId == organizationId && candidate.Key == key,
            ct))
        {
            throw new ConflictException("PROJECT_KEY_EXISTS", "Project key must be unique inside the organization.");
        }

        var now = clock.UtcNow;
        var project = new ProjectDocument
        {
            OrganizationId = organizationId,
            Key = key,
            Name = NormalizeName(request.Name),
            Visibility = ProjectVisibilities.Normalize(request.Visibility),
            CreatedAt = now,
            UpdatedAt = now,
            Members =
            [
                new ProjectMemberDocument { UserId = ownerUserId, Role = ProjectRoles.Owner }
            ]
        };
        try
        {
            await projects.CreateAsync(project, ct);
        }
        catch (DocumentConflictException)
        {
            throw new ConflictException(
                "PROJECT_KEY_EXISTS",
                "Project key must be unique inside the organization.");
        }

        await audit.WriteAsync("ProjectCreated", project.Id, null, $"{project.Key}:{project.Name}", correlationId, ct);
        return ProjectResponseMapper.ToResponse(project);
    }

    private void EnsureOrganizationScope(string organizationId)
    {
        if (!IsSystemAdmin()
            && !string.Equals(currentUser.OrganizationId, organizationId.Trim(), StringComparison.Ordinal))
        {
            throw new ForbiddenException("User cannot access projects outside the current organization.");
        }
    }

    private string CurrentUserId() =>
        !string.IsNullOrWhiteSpace(currentUser.UserId)
            ? currentUser.UserId
            : throw new UnauthorizedException("Authenticated user is required.");

    private bool IsSystemAdmin() => PermissionCatalog.IsSystemAdministrator(currentUser.Roles);

    private static string NormalizeName(string? name)
    {
        var normalized = name?.Trim() ?? string.Empty;
        if (normalized.Length is < 2 or > 120)
        {
            throw new ValidationException("Project name must contain 2-120 characters.");
        }

        return normalized;
    }

    private static string NormalizeKey(string? key)
    {
        var normalized = key?.Trim().ToUpperInvariant() ?? string.Empty;
        if (!ProjectKeyPattern().IsMatch(normalized))
        {
            throw new ValidationException("Project key must contain 2-10 upper-case letters, numbers or hyphens.");
        }

        return normalized;
    }

    [GeneratedRegex("^[A-Z0-9][A-Z0-9-]{0,8}[A-Z0-9]$")]
    private static partial Regex ProjectKeyPattern();
}
