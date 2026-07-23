using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Zumbo.Modules.Audit;
using Zumbo.Modules.Boards;
using Zumbo.Modules.Identity;
using Zumbo.Modules.Organizations;
using Zumbo.Modules.Projects;
using Zumbo.Modules.Teams;
using Zumbo.SharedKernel;

namespace Zumbo.ApiTests;

public sealed class ProjectLifecycleApiTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient client;

    public ProjectLifecycleApiTests(WebApplicationFactory<Program> factory)
    {
        client = factory.CreateClient();
    }

    [Fact]
    public async Task Lifecycle_EnforcesKeyVisibilityOwnershipCatalogReleaseRetentionAndAudit()
    {
        var stamp = Guid.NewGuid().ToString("N");
        var organizationId = "project-domain003-" + stamp;
        var owner = await RegisterAsync("project-owner-" + stamp, organizationId);
        var member = await RegisterAsync("project-member-" + stamp, organizationId);
        var observer = await RegisterAsync("project-observer-" + stamp, organizationId);
        var foreign = await RegisterAsync("project-foreign-" + stamp, "foreign-" + stamp);
        Authenticate(owner);

        var missingOrganization = await client.PostAsJsonAsync("/api/projects", new CreateProjectRequest(
            organizationId,
            "MISS",
            "Missing organization",
            owner.User.Id));
        await AssertErrorAsync(missingOrganization, HttpStatusCode.NotFound, "ORGANIZATION_NOT_FOUND");

        var organization = await PostAsync<OrganizationResponse>(
            "/api/organizations",
            new CreateOrganizationRequest("Project Domain 003", organizationId));
        var createResponse = await client.PostAsJsonAsync("/api/projects", new CreateProjectRequest(
            organizationId,
            "LIFE",
            "Lifecycle Project",
            owner.User.Id));
        createResponse.EnsureSuccessStatusCode();
        var project = (await createResponse.Content.ReadFromJsonAsync<ApiResponse<ProjectResponse>>())!.Data!;
        Assert.Equal("\"1\"", createResponse.Headers.ETag?.Tag);

        var duplicate = await client.PostAsJsonAsync("/api/projects", new CreateProjectRequest(
            organizationId,
            "life",
            "Duplicate key",
            owner.User.Id));
        await AssertErrorAsync(duplicate, HttpStatusCode.Conflict, "PROJECT_KEY_EXISTS");

        var internalBoard = await PostAsync<BoardResponse>(
            "/api/boards",
            new CreateBoardRequest(project.Id, "Internal Board", "Kanban"));
        Authenticate(observer);
        var visibleProjects = await GetAsync<IReadOnlyList<ProjectResponse>>(
            $"/api/projects?organizationId={organizationId}");
        Assert.Contains(visibleProjects, candidate => candidate.Id == project.Id);
        _ = await GetAsync<ProjectResponse>($"/api/projects/{project.Id}");
        var visibleBoards = await client.GetAsync($"/api/boards/by-project/{project.Id}");
        Assert.Equal(HttpStatusCode.OK, visibleBoards.StatusCode);
        Assert.NotNull(internalBoard);

        Authenticate(owner);
        using (var immutable = VersionedRequest(
            HttpMethod.Put,
            $"/api/projects/{project.Id}",
            new UpdateProjectRequest("Lifecycle Project", ProjectVisibilities.Private, "RENAMED"),
            project.Version))
        {
            await AssertErrorAsync(
                await client.SendAsync(immutable),
                HttpStatusCode.Conflict,
                "PROJECT_KEY_IMMUTABLE");
        }

        project = await SendVersionedAsync(
            HttpMethod.Put,
            $"/api/projects/{project.Id}",
            new UpdateProjectRequest("Private Lifecycle Project", ProjectVisibilities.Private),
            project.Version);
        Authenticate(observer);
        visibleProjects = await GetAsync<IReadOnlyList<ProjectResponse>>(
            $"/api/projects?organizationId={organizationId}");
        Assert.DoesNotContain(visibleProjects, candidate => candidate.Id == project.Id);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/projects/{project.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(
            $"/api/boards/by-project/{project.Id}")).StatusCode);

        Authenticate(owner);
        using (var foreignGrant = VersionedRequest(
            HttpMethod.Post,
            $"/api/projects/{project.Id}/members",
            new AddProjectMemberRequest(foreign.User.Id, ProjectRoles.Developer),
            project.Version))
        {
            await AssertErrorAsync(
                await client.SendAsync(foreignGrant),
                HttpStatusCode.Conflict,
                "PROJECT_MEMBER_ORGANIZATION_MISMATCH");
        }

        project = await SendVersionedAsync(
            HttpMethod.Post,
            $"/api/projects/{project.Id}/members",
            new AddProjectMemberRequest(member.User.Id, ProjectRoles.Admin),
            project.Version);
        var team = await PostAsync<TeamResponse>(
            "/api/teams",
            new CreateTeamRequest(organizationId, "Project Team", owner.User.Id));
        project = await SendVersionedAsync(
            HttpMethod.Post,
            $"/api/projects/{project.Id}/teams",
            new AddProjectTeamRequest(team.Id),
            project.Version);
        Assert.Contains(team.Id, project.TeamIds);

        var archivedTeam = await PostAsync<TeamResponse>(
            "/api/teams",
            new CreateTeamRequest(organizationId, "Archived Team", owner.User.Id));
        using (var archiveTeam = VersionedRequest(
            HttpMethod.Delete,
            $"/api/teams/{archivedTeam.Id}",
            null,
            archivedTeam.Version))
        {
            (await client.SendAsync(archiveTeam)).EnsureSuccessStatusCode();
        }
        using (var archivedTeamGrant = VersionedRequest(
            HttpMethod.Post,
            $"/api/projects/{project.Id}/teams",
            new AddProjectTeamRequest(archivedTeam.Id),
            project.Version))
        {
            await AssertErrorAsync(
                await client.SendAsync(archivedTeamGrant),
                HttpStatusCode.Conflict,
                "PROJECT_TEAM_ORGANIZATION_MISMATCH");
        }

        var staleVersion = project.Version;
        project = await SendVersionedAsync(
            HttpMethod.Post,
            $"/api/projects/{project.Id}/ownership-transfer",
            new TransferProjectOwnershipRequest(member.User.Id),
            project.Version);
        Assert.Single(project.Members, candidate => candidate.Role == ProjectRoles.Owner);
        Assert.Contains(project.Members, candidate =>
            candidate.UserId == member.User.Id && candidate.Role == ProjectRoles.Owner);
        using (var staleUpdate = VersionedRequest(
            HttpMethod.Put,
            $"/api/projects/{project.Id}",
            new UpdateProjectRequest("Stale", ProjectVisibilities.Private),
            staleVersion))
        {
            await AssertErrorAsync(
                await client.SendAsync(staleUpdate),
                HttpStatusCode.Conflict,
                "CONCURRENCY_CONFLICT");
        }

        using (var formerOwnerTransfer = VersionedRequest(
            HttpMethod.Post,
            $"/api/projects/{project.Id}/ownership-transfer",
            new TransferProjectOwnershipRequest(owner.User.Id),
            project.Version))
        {
            Assert.Equal(HttpStatusCode.Forbidden, (await client.SendAsync(formerOwnerTransfer)).StatusCode);
        }
        Authenticate(member);
        using (var ownerRoleChange = VersionedRequest(
            HttpMethod.Patch,
            $"/api/projects/{project.Id}/members/{member.User.Id}/role",
            new ChangeProjectMemberRoleRequest(ProjectRoles.Admin),
            project.Version))
        {
            await AssertErrorAsync(
                await client.SendAsync(ownerRoleChange),
                HttpStatusCode.Conflict,
                "PROJECT_OWNER_ROLE_LOCKED");
        }

        using (var oversizedTemplate = VersionedRequest(
            HttpMethod.Post,
            $"/api/projects/{project.Id}/templates",
            new UpsertProjectTemplateRequest(
                "Oversized",
                true,
                Enumerable.Range(1, ProjectCatalogLimits.MaximumDefaultComponentNames + 1)
                    .Select(index => $"Component {index}")
                    .ToArray()),
            project.Version))
        {
            await AssertErrorAsync(
                await client.SendAsync(oversizedTemplate),
                HttpStatusCode.BadRequest,
                "VALIDATION_ERROR");
        }
        var unchangedProject = await GetAsync<ProjectResponse>($"/api/projects/{project.Id}");
        Assert.Equal(project.Version, unchangedProject.Version);
        Assert.Empty(unchangedProject.Templates!);

        project = await SendVersionedAsync(
            HttpMethod.Post,
            $"/api/projects/{project.Id}/templates",
            new UpsertProjectTemplateRequest("Delivery", true, ["API", "Web"]),
            project.Version);
        var firstTemplate = Assert.Single(project.Templates!);
        project = await SendVersionedAsync(
            HttpMethod.Post,
            $"/api/projects/{project.Id}/templates",
            new UpsertProjectTemplateRequest("Support", true, ["Operations"]),
            project.Version);
        Assert.Single(project.Templates!, template => template.IsDefault);
        project = await SendVersionedAsync(
            HttpMethod.Delete,
            $"/api/projects/{project.Id}/templates/{firstTemplate.Id}",
            null,
            project.Version);
        Assert.Contains(project.Templates!, template => template.Id == firstTemplate.Id && template.Archived);

        project = await SendVersionedAsync(
            HttpMethod.Post,
            $"/api/projects/{project.Id}/components",
            new CreateProjectComponentRequest("API", "Public API"),
            project.Version);
        var component = Assert.Single(project.Components!);
        project = await SendVersionedAsync(
            HttpMethod.Put,
            $"/api/projects/{project.Id}/components/{component.Id}",
            new UpdateProjectComponentRequest("Platform API", "Core API"),
            project.Version);
        Assert.Equal("Platform API", Assert.Single(project.Components!).Name);

        project = await SendVersionedAsync(
            HttpMethod.Post,
            $"/api/projects/{project.Id}/versions",
            new CreateProjectVersionRequest("1.0"),
            project.Version);
        var version = Assert.Single(project.Versions!);
        project = await SendVersionedAsync(
            HttpMethod.Post,
            $"/api/projects/{project.Id}/releases",
            new CreateProjectReleaseRequest(version.Id, "Version 1.0"),
            project.Version);
        var release = Assert.Single(project.Releases!);
        using (var prematurePublish = VersionedRequest(
            HttpMethod.Post,
            $"/api/projects/{project.Id}/releases/{release.Id}/publish",
            null,
            project.Version))
        {
            await AssertErrorAsync(
                await client.SendAsync(prematurePublish),
                HttpStatusCode.Conflict,
                "PROJECT_RELEASE_NOT_APPROVED");
        }
        project = await SendVersionedAsync(
            HttpMethod.Post,
            $"/api/projects/{project.Id}/releases/{release.Id}/approve",
            null,
            project.Version);
        project = await SendVersionedAsync(
            HttpMethod.Post,
            $"/api/projects/{project.Id}/releases/{release.Id}/publish",
            null,
            project.Version);
        Assert.Equal(ProjectReleaseStatuses.Published, Assert.Single(project.Releases!).Status);
        Assert.Equal(ProjectVersionStatuses.Released, Assert.Single(project.Versions!).Status);

        project = await SendVersionedAsync(
            HttpMethod.Post,
            $"/api/projects/{project.Id}/milestones",
            new CreateProjectMilestoneRequest("Launch", DateTimeOffset.UtcNow.AddDays(30)),
            project.Version);
        var milestone = Assert.Single(project.Milestones!);
        project = await SendVersionedAsync(
            HttpMethod.Put,
            $"/api/projects/{project.Id}/milestones/{milestone.Id}",
            new UpdateProjectMilestoneRequest("General Availability", DateTimeOffset.UtcNow.AddDays(45)),
            project.Version);
        project = await SendVersionedAsync(
            HttpMethod.Post,
            $"/api/projects/{project.Id}/milestones/{milestone.Id}/complete",
            null,
            project.Version);
        Assert.Equal(ProjectMilestoneStatuses.Completed, Assert.Single(project.Milestones!).Status);

        using (var archive = VersionedRequest(HttpMethod.Delete, $"/api/projects/{project.Id}", null, project.Version))
        {
            (await client.SendAsync(archive)).EnsureSuccessStatusCode();
        }
        var archived = Assert.Single(
            await GetAsync<IReadOnlyList<ProjectResponse>>(
                $"/api/projects?organizationId={organizationId}&archived=true"),
            candidate => candidate.Id == project.Id);
        Assert.NotNull(archived.ArchivedAt);
        Assert.Equal(90, (archived.RetainUntil!.Value - archived.ArchivedAt!.Value).TotalDays);
        project = await SendVersionedAsync(
            HttpMethod.Post,
            $"/api/projects/{project.Id}/restore",
            null,
            archived.Version);
        Assert.False(project.Archived);
        Assert.Null(project.RetainUntil);

        var audit = await GetAsync<AuditLogPageResponse>(
            $"/api/audit?entityType=Project&entityId={project.Id}&page=1&pageSize=100");
        Assert.Contains(audit.Items, entry => entry.Action == "ProjectOwnershipTransferred");
        Assert.Contains(audit.Items, entry => entry.Action == "ProjectTemplateCreated");
        Assert.Contains(audit.Items, entry => entry.Action == "ProjectComponentCreated");
        Assert.Contains(audit.Items, entry => entry.Action == "ProjectReleasePublished");
        Assert.Contains(audit.Items, entry => entry.Action == "ProjectMilestoneCompleted");
        Assert.Contains(audit.Items, entry => entry.Action == "ProjectArchived");
        Assert.Contains(audit.Items, entry => entry.Action == "ProjectRestored");

        Authenticate(owner);
        organization = await SendVersionedOrganizationAsync(
            $"/api/organizations/{organization.Id}/suspend",
            new SuspendOrganizationRequest("project lifecycle complete"),
            organization.Version);
        Assert.Equal(OrganizationStatuses.Suspended, organization.Status);
        Authenticate(member);
        using var inactiveUpdate = VersionedRequest(
            HttpMethod.Put,
            $"/api/projects/{project.Id}",
            new UpdateProjectRequest("Blocked", ProjectVisibilities.Private),
            project.Version);
        await AssertErrorAsync(
            await client.SendAsync(inactiveUpdate),
            HttpStatusCode.Conflict,
            "PROJECT_ORGANIZATION_INACTIVE");
    }

    private async Task<AuthResponse> RegisterAsync(string username, string organizationId) =>
        await PostAsync<AuthResponse>("/api/auth/register", new RegisterUserRequest(
            username,
            $"{username}@zumbo.local",
            "P@ssword123",
            organizationId));

    private void Authenticate(AuthResponse authentication) =>
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", authentication.AccessToken);

    private async Task<T> PostAsync<T>(string url, object request)
    {
        var response = await client.PostAsJsonAsync(url, request);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ApiResponse<T>>())!.Data!;
    }

    private async Task<T> GetAsync<T>(string url)
    {
        var response = await client.GetAsync(url);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ApiResponse<T>>())!.Data!;
    }

    private async Task<ProjectResponse> SendVersionedAsync(
        HttpMethod method,
        string url,
        object? body,
        long expectedVersion)
    {
        using var request = VersionedRequest(method, url, body, expectedVersion);
        var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        var envelope = await response.Content.ReadFromJsonAsync<ApiResponse<ProjectResponse>>();
        Assert.Equal($"\"{envelope!.Data!.Version}\"", response.Headers.ETag?.Tag);
        return envelope.Data;
    }

    private async Task<OrganizationResponse> SendVersionedOrganizationAsync(
        string url,
        object body,
        long expectedVersion)
    {
        using var request = VersionedRequest(HttpMethod.Post, url, body, expectedVersion);
        var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ApiResponse<OrganizationResponse>>())!.Data!;
    }

    private static HttpRequestMessage VersionedRequest(
        HttpMethod method,
        string url,
        object? body,
        long expectedVersion)
    {
        var request = new HttpRequestMessage(method, url)
        {
            Content = body is null ? null : JsonContent.Create(body)
        };
        request.Headers.TryAddWithoutValidation("If-Match", $"\"{expectedVersion}\"");
        return request;
    }

    private static async Task AssertErrorAsync(
        HttpResponseMessage response,
        HttpStatusCode status,
        string code)
    {
        Assert.Equal(status, response.StatusCode);
        var envelope = await response.Content.ReadFromJsonAsync<ApiResponse<object>>();
        Assert.Equal(code, envelope!.Error!.Code);
    }
}
