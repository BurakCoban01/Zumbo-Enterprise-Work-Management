using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Zumbo.Modules.Boards;
using Zumbo.Modules.Identity;
using Zumbo.Modules.Organizations;
using Zumbo.Modules.Projects;
using Zumbo.Modules.WorkItems;
using Zumbo.SharedKernel;

namespace Zumbo.ApiTests;

public sealed class PermissionHttpMatrixTests(WebApplicationFactory<Program> baseFactory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    [Fact]
    public async Task ResourceMatrix_EnforcesRoleTenantInactiveAndSystemAdminRules()
    {
        using var factory = baseFactory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["IdentityBootstrap:BootstrapToken"] = "permission-matrix-bootstrap"
                }));
        });
        using var client = factory.CreateClient();
        var stamp = Guid.NewGuid().ToString("N");
        var organizationId = "permission-org-" + stamp;

        var admin = await RegisterAsync(client, new RegisterUserRequest(
            "permission-admin-" + stamp,
            "admin@zumbo.local",
            "P@ssword123",
            "system-" + stamp,
            "permission-matrix-bootstrap"));
        var owner = await RegisterAsync(client, new RegisterUserRequest(
            "permission-owner-" + stamp,
            $"permission-owner-{stamp}@zumbo.local",
            "P@ssword123",
            organizationId));
        var viewer = await RegisterAsync(client, new RegisterUserRequest(
            "permission-viewer-" + stamp,
            $"permission-viewer-{stamp}@zumbo.local",
            "P@ssword123",
            organizationId));
        var developer = await RegisterAsync(client, new RegisterUserRequest(
            "permission-developer-" + stamp,
            $"permission-developer-{stamp}@zumbo.local",
            "P@ssword123",
            organizationId));
        var outsider = await RegisterAsync(client, new RegisterUserRequest(
            "permission-outsider-" + stamp,
            $"permission-outsider-{stamp}@zumbo.local",
            "P@ssword123",
            "foreign-" + stamp));

        Authenticate(client, owner.AccessToken);
        _ = await PostAsync<OrganizationResponse>(client, "/api/organizations", new CreateOrganizationRequest(
            "Permission Organization",
            organizationId));
        var project = await PostAsync<ProjectResponse>(client, "/api/projects", new CreateProjectRequest(
            organizationId,
            "PM" + stamp[..6].ToUpperInvariant(),
            "Permission Matrix",
            owner.User.Id));
        _ = await PostAsync<ProjectResponse>(client, $"/api/projects/{project.Id}/members", new AddProjectMemberRequest(
            viewer.User.Id,
            "Viewer"));
        _ = await PostAsync<ProjectResponse>(client, $"/api/projects/{project.Id}/members", new AddProjectMemberRequest(
            developer.User.Id,
            "Developer"));
        var board = await PostAsync<BoardResponse>(client, "/api/boards", new CreateBoardRequest(
            project.Id,
            "Permission Matrix Board",
            "Kanban"));
        var ownerWorkItem = await PostAsync<WorkItemResponse>(client, "/api/work-items", new CreateWorkItemRequest(
            project.Id,
            board.Id,
            "Resource tenant activity",
            "Task",
            "Medium",
            null,
            null));
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var ownerSprint = await PostAsync<SprintResponse>(client, "/api/sprints", new CreateSprintRequest(
            project.Id,
            "Permission Sprint",
            "Resource permission coverage",
            today,
            today.AddDays(6)));
        var ownerSchema = await GetAsync<WorkItemTypeSchemaResponse>(
            client,
            $"/api/work-item-schemas/{project.Id}");
        var schemaRequest = new UpsertWorkItemTypeSchemaRequest(
            ownerSchema.IssueTypes,
            ownerSchema.CustomFields,
            ownerSchema.Layouts);

        Authenticate(client, viewer.AccessToken);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/work-items?projectId={project.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/boards/by-project/{project.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/sprints/{ownerSprint.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(
            $"/api/sprints/projects/{project.Id}/backlog")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(
            $"/api/work-item-schemas/{project.Id}")).StatusCode);
        var viewerWrite = await client.PostAsJsonAsync("/api/work-items", new CreateWorkItemRequest(
            project.Id,
            board.Id,
            "Viewer cannot create",
            "Task",
            "Medium",
            null,
            null));
        Assert.Equal(HttpStatusCode.Forbidden, viewerWrite.StatusCode);
        var viewerBoardWrite = await client.PostAsJsonAsync("/api/boards", new CreateBoardRequest(
            project.Id,
            "Viewer cannot manage boards",
            "Kanban"));
        Assert.Equal(HttpStatusCode.Forbidden, viewerBoardWrite.StatusCode);
        var viewerSprintWrite = await client.PostAsJsonAsync("/api/sprints", new CreateSprintRequest(
            project.Id,
            "Viewer cannot create sprint",
            null,
            today.AddDays(7),
            today.AddDays(13)));
        Assert.Equal(HttpStatusCode.Forbidden, viewerSprintWrite.StatusCode);
        var viewerSchemaWrite = await client.PutAsJsonAsync(
            $"/api/work-item-schemas/{project.Id}",
            schemaRequest);
        Assert.Equal(HttpStatusCode.Forbidden, viewerSchemaWrite.StatusCode);
        _ = await PostAsync<WorkItemResponse>(client, $"/api/work-items/{ownerWorkItem.Id}/comments", new AddCommentRequest(
            "Viewer comment in resource tenant",
            []));

        Authenticate(client, developer.AccessToken);
        _ = await PostAsync<WorkItemResponse>(client, "/api/work-items", new CreateWorkItemRequest(
            project.Id,
            board.Id,
            "Developer can create",
            "Task",
            "Medium",
            null,
            null));
        var developerBoardWrite = await client.PostAsJsonAsync("/api/boards", new CreateBoardRequest(
            project.Id,
            "Developer cannot manage boards",
            "Kanban"));
        Assert.Equal(HttpStatusCode.Forbidden, developerBoardWrite.StatusCode);
        _ = await PostAsync<SprintResponse>(client, "/api/sprints", new CreateSprintRequest(
            project.Id,
            "Developer Sprint",
            null,
            today.AddDays(7),
            today.AddDays(13)));

        Authenticate(client, outsider.AccessToken);
        var foreignSearch = await client.GetAsync($"/api/work-items?projectId={project.Id}&text=resource");
        Assert.Equal(HttpStatusCode.NotFound, foreignSearch.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(
            $"/api/work-items/{ownerWorkItem.Id}/comments")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/sprints/{ownerSprint.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(
            $"/api/work-item-schemas/{project.Id}")).StatusCode);
        var foreignAudit = await client.GetAsync($"/api/audit?entityType=Project&entityId={project.Id}");
        Assert.Equal(HttpStatusCode.Forbidden, foreignAudit.StatusCode);
        var foreignOperations = await client.GetAsync("/api/work-items/durable-messaging/metrics");
        Assert.Equal(HttpStatusCode.Forbidden, foreignOperations.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(
            $"/api/notifications/delivery/status?organizationId={organizationId}")).StatusCode);

        Authenticate(client, admin.AccessToken);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/work-items?projectId={project.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/sprints/{ownerSprint.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(
            $"/api/work-item-schemas/{project.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/audit?entityType=Project&entityId={project.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/work-items/durable-messaging/metrics")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(
            $"/api/notifications/delivery/status?organizationId={organizationId}")).StatusCode);
        var adminComments = await GetAsync<WorkItemActivityPage<CommentResponse>>(
            client,
            $"/api/work-items/{ownerWorkItem.Id}/comments");
        Assert.Contains(adminComments.Items, x => x.Body == "Viewer comment in resource tenant");

        Authenticate(client, viewer.AccessToken);
        var deactivated = await client.PostAsJsonAsync("/api/auth/deactivate", new DeactivateAccountRequest("P@ssword123"));
        Assert.Equal(HttpStatusCode.OK, deactivated.StatusCode);
        var inactiveAccess = await client.GetAsync($"/api/work-items?projectId={project.Id}");
        Assert.Equal(HttpStatusCode.Unauthorized, inactiveAccess.StatusCode);
    }

    private static async Task<AuthResponse> RegisterAsync(HttpClient client, RegisterUserRequest request) =>
        await PostAsync<AuthResponse>(client, "/api/auth/register", request);

    private static async Task<T> PostAsync<T>(HttpClient client, string path, object request)
    {
        var response = await client.PostAsJsonAsync(path, request);
        response.EnsureSuccessStatusCode();
        var envelope = await response.Content.ReadFromJsonAsync<ApiResponse<T>>();
        return envelope!.Data!;
    }

    private static async Task<T> GetAsync<T>(HttpClient client, string path)
    {
        var response = await client.GetAsync(path);
        response.EnsureSuccessStatusCode();
        var envelope = await response.Content.ReadFromJsonAsync<ApiResponse<T>>();
        return envelope!.Data!;
    }

    private static void Authenticate(HttpClient client, string token) =>
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
}
