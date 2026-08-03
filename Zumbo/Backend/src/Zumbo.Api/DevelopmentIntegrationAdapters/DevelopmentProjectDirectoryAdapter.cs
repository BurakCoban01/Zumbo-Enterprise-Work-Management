using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.Modules.Projects;
using Zumbo.Modules.WorkItems;
using Zumbo.SharedKernel;

public sealed class DevelopmentProjectDirectoryAdapter(
    IDocumentRepository<ProjectDocument> projects,
    IProjectResourcePolicy resourcePolicy) : IDevelopmentProjectDirectory
{
    public async Task<DevelopmentProjectResource> GetAsync(
        string organizationId,
        string projectId,
        CancellationToken ct)
    {
        var access = await resourcePolicy.AuthorizeAsync(
            projectId,
            PermissionCatalog.WorkItemLink,
            ct);
        if (!string.Equals(access.OrganizationId, organizationId, StringComparison.Ordinal))
        {
            throw new NotFoundException(
                "PROJECT_NOT_FOUND",
                "Project was not found.");
        }

        var project = await projects.SelectAsync(
            item => item.Id == projectId
                && item.OrganizationId == organizationId
                && !item.Archived,
            ct) ?? throw new NotFoundException(
                "PROJECT_NOT_FOUND",
                "Project was not found.");
        return new DevelopmentProjectResource(
            project.OrganizationId,
            project.Id,
            project.Key,
            project.Name);
    }
}
