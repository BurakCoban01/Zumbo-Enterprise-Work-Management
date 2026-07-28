using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.Modules.Identity;
using Zumbo.Modules.Organizations;
using Zumbo.Modules.Projects;
using Zumbo.Modules.WorkItems;
using Zumbo.SharedKernel;

namespace Zumbo.ApiTests;

public sealed class DashboardApiTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> factory;

    public DashboardApiTests(WebApplicationFactory<Program> factory)
    {
        this.factory = factory;
    }

    [Fact]
    public async Task LifecycleRenderSharingExportAndIsolationAreAuthoritative()
    {
        using var client = factory.CreateClient();
        var stamp = Guid.NewGuid().ToString("N");
        var organizationId = "dashboard-" + stamp;
        var owner = await RegisterAsync(client, "dashboard-owner-" + stamp, organizationId);
        var viewer = await RegisterAsync(client, "dashboard-viewer-" + stamp, organizationId);
        var outsider = await RegisterAsync(client, "dashboard-outsider-" + stamp, "foreign-" + stamp);
        Authorize(client, owner);
        await PostAsync<OrganizationResponse>(
            client,
            "/api/organizations",
            new CreateOrganizationRequest("Dashboard organization", organizationId));
        var project = await PostAsync<ProjectResponse>(
            client,
            "/api/projects",
            new CreateProjectRequest(
                organizationId,
                "DA" + stamp[..6],
                "Dashboard project",
                owner.User.Id));
        await PostAsync<ProjectResponse>(
            client,
            $"/api/projects/{project.Id}/members",
            new AddProjectMemberRequest(viewer.User.Id, ProjectRoles.Viewer));

        var createResponse = await client.PostAsJsonAsync(
            "/api/dashboards",
            Definition(project.Id, "Delivery pulse"));
        createResponse.EnsureSuccessStatusCode();
        Assert.Equal("\"1\"", createResponse.Headers.ETag?.Tag);
        var dashboard = (await createResponse.Content.ReadFromJsonAsync<
            ApiResponse<DashboardResponse>>())!.Data!;
        Assert.True(dashboard.CanEdit);

        var renderResponse = await client.GetAsync($"/api/dashboards/{dashboard.Id}/render");
        renderResponse.EnsureSuccessStatusCode();
        var render = (await renderResponse.Content.ReadFromJsonAsync<
            ApiResponse<DashboardRenderResponse>>())!.Data!;
        var renderedWidget = Assert.Single(render.Widgets);
        Assert.Equal("Ready", renderedWidget.Status);
        Assert.Equal(["total", "done", "inProgress", "overdue"],
            Assert.Single(renderedWidget.Sources).Columns.Select(column => column.Key));
        Assert.NotNull(render.GeneratedAt);
        Assert.False(render.Partial);

        DashboardDocument degradedDocument;
        using (var scope = factory.Services.CreateScope())
        {
            var repository = scope.ServiceProvider
                .GetRequiredService<IDocumentRepository<DashboardDocument>>();
            degradedDocument = await repository.CreateAsync(new DashboardDocument
            {
                OrganizationId = organizationId,
                OwnerUserId = owner.User.Id,
                Name = "Degraded dashboard",
                Scope = DashboardScopes.Personal,
                ProjectIds = [project.Id],
                Widgets =
                [
                    new DashboardWidgetDocument
                    {
                        Id = "legacy-widget",
                        Type = "RemovedLegacyType",
                        Title = "Legacy widget",
                        Column = 1,
                        Row = 1,
                        Width = 12,
                        Height = 2
                    }
                ],
                Filter = new DashboardFilterDocument(),
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            }, CancellationToken.None);
        }
        var degradedResponse = await client.GetAsync(
            $"/api/dashboards/{degradedDocument.Id}/render");
        degradedResponse.EnsureSuccessStatusCode();
        var degraded = (await degradedResponse.Content.ReadFromJsonAsync<
            ApiResponse<DashboardRenderResponse>>())!.Data!;
        var degradedWidget = Assert.Single(degraded.Widgets);
        Assert.Equal("Degraded", degradedWidget.Status);
        Assert.Equal("DASHBOARD_WIDGET_SOURCE_UNAVAILABLE", degradedWidget.ErrorCode);
        Assert.Empty(degradedWidget.Sources);
        Assert.True(degraded.Partial);
        Assert.Null(degraded.GeneratedAt);

        using (var sharing = new HttpRequestMessage(
            HttpMethod.Put,
            $"/api/dashboards/{dashboard.Id}/sharing")
        {
            Content = JsonContent.Create(new ShareDashboardRequest([viewer.User.Id]))
        })
        {
            sharing.Headers.IfMatch.Add(new EntityTagHeaderValue("\"1\""));
            var shared = await client.SendAsync(sharing);
            shared.EnsureSuccessStatusCode();
            Assert.Equal("\"2\"", shared.Headers.ETag?.Tag);
        }

        using (var stale = new HttpRequestMessage(
            HttpMethod.Put,
            $"/api/dashboards/{dashboard.Id}")
        {
            Content = JsonContent.Create(Definition(project.Id, "Stale edit"))
        })
        {
            stale.Headers.IfMatch.Add(new EntityTagHeaderValue("\"1\""));
            await AssertErrorAsync(
                await client.SendAsync(stale),
                HttpStatusCode.Conflict,
                "CONCURRENCY_CONFLICT");
        }

        Authorize(client, viewer);
        var viewerGet = await client.GetAsync($"/api/dashboards/{dashboard.Id}");
        viewerGet.EnsureSuccessStatusCode();
        var viewerDashboard = (await viewerGet.Content.ReadFromJsonAsync<
            ApiResponse<DashboardResponse>>())!.Data!;
        Assert.False(viewerDashboard.CanEdit);
        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await client.PutAsJsonAsync(
                $"/api/dashboards/{dashboard.Id}",
                Definition(project.Id, "Viewer edit"))).StatusCode);
        var export = await client.GetAsync($"/api/dashboards/{dashboard.Id}/export");
        export.EnsureSuccessStatusCode();
        Assert.Equal("application/json", export.Content.Headers.ContentType?.MediaType);
        Assert.Contains("zumbo-dashboard-", export.Content.Headers.ContentDisposition?.FileName);

        Authorize(client, outsider);
        await AssertErrorAsync(
            await client.GetAsync($"/api/dashboards/{dashboard.Id}"),
            HttpStatusCode.NotFound,
            "DASHBOARD_NOT_FOUND");

        client.DefaultRequestHeaders.Authorization = null;
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await client.GetAsync("/api/dashboards")).StatusCode);
    }

    private static SaveDashboardRequest Definition(string projectId, string name) =>
        new(
            name,
            "Synthetic delivery dashboard",
            DashboardScopes.Personal,
            [projectId],
            [
                new DashboardWidgetRequest(
                    "summary",
                    DashboardWidgetTypes.ProjectSummary,
                    "Project summary",
                    1,
                    1,
                    12,
                    2,
                    projectId)
            ],
            new DashboardFilterRequest(30, 30));

    private static async Task<AuthResponse> RegisterAsync(
        HttpClient client,
        string username,
        string organizationId) =>
        await PostAsync<AuthResponse>(
            client,
            "/api/auth/register",
            new RegisterUserRequest(
                username,
                username + "@zumbo.local",
                "P@ssword123",
                organizationId));

    private static void Authorize(HttpClient client, AuthResponse auth) =>
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", auth.AccessToken);

    private static async Task<T> PostAsync<T>(HttpClient client, string url, object body)
    {
        var response = await client.PostAsJsonAsync(url, body);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ApiResponse<T>>())!.Data!;
    }

    private static async Task AssertErrorAsync(
        HttpResponseMessage response,
        HttpStatusCode status,
        string code)
    {
        Assert.Equal(status, response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<ApiResponse<JsonElement>>();
        Assert.Equal(code, error!.Error!.Code);
    }
}
