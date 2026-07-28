using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Zumbo.Modules.Identity;
using Zumbo.Modules.Organizations;
using Zumbo.Modules.Projects;
using Zumbo.Modules.WorkItems;
using Zumbo.SharedKernel;

namespace Zumbo.ApiTests;

public sealed class CapacityPlanningApiTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> factory;

    public CapacityPlanningApiTests(WebApplicationFactory<Program> factory)
    {
        this.factory = factory;
    }

    [Fact]
    public async Task LifecycleSnapshotScenarioSharingConcurrencyAndIsolationAreAuthoritative()
    {
        using var client = factory.CreateClient();
        var stamp = Guid.NewGuid().ToString("N");
        var organizationId = "capacity-" + stamp;
        var owner = await RegisterAsync(client, "capacity-owner-" + stamp, organizationId);
        var viewer = await RegisterAsync(client, "capacity-viewer-" + stamp, organizationId);
        var outsider = await RegisterAsync(client, "capacity-outsider-" + stamp, "foreign-" + stamp);
        Authorize(client, owner);
        await PostAsync<OrganizationResponse>(
            client,
            "/api/organizations",
            new CreateOrganizationRequest("Capacity organization", organizationId));
        var firstProject = await CreateProjectAsync(
            client,
            organizationId,
            owner.User.Id,
            "CA" + stamp[..6]);
        var secondProject = await CreateProjectAsync(
            client,
            organizationId,
            owner.User.Id,
            "CB" + stamp[..6]);
        await AddViewerAsync(client, firstProject.Id, viewer.User.Id);
        await AddViewerAsync(client, secondProject.Id, viewer.User.Id);

        var definition = Definition(
            owner.User.Id,
            firstProject.Id,
            secondProject.Id,
            []);
        var createResponse = await client.PostAsJsonAsync("/api/capacity-plans", definition);
        Assert.True(
            createResponse.IsSuccessStatusCode,
            await createResponse.Content.ReadAsStringAsync());
        Assert.Equal("\"1\"", createResponse.Headers.ETag?.Tag);
        var plan = (await createResponse.Content.ReadFromJsonAsync<
            ApiResponse<CapacityPlanResponse>>())!.Data!;
        Assert.True(plan.CanEdit);
        Assert.Single(plan.Allocations);

        var snapshot = await GetAsync<CapacitySnapshotResponse>(
            client,
            $"/api/capacity-plans/{plan.Id}/snapshot");
        Assert.Equal(CapacitySnapshotStatuses.Ready, snapshot.SourceStatus);
        Assert.Equal(80, snapshot.Summary.CapacityHours);
        Assert.Equal(48, snapshot.Summary.AllocatedHours);
        Assert.Equal(2, snapshot.Projects.Count);

        var scenarioResponse = await client.PostAsJsonAsync(
            $"/api/capacity-plans/{plan.Id}/scenarios",
            new CapacityScenarioRequest(
            [
                definition.Allocations.Single(),
                new CapacityAllocationRequest(
                    null,
                    owner.User.Id,
                    secondProject.Id,
                    new DateOnly(2026, 8, 3),
                    new DateOnly(2026, 8, 16),
                    50)
            ]));
        scenarioResponse.EnsureSuccessStatusCode();
        var scenario = (await scenarioResponse.Content.ReadFromJsonAsync<
            ApiResponse<CapacityScenarioResponse>>())!.Data!;
        Assert.Equal(48, scenario.Baseline.Summary.AllocatedHours);
        Assert.Equal(88, scenario.Candidate.Summary.AllocatedHours);
        Assert.Equal(
            CapacityLoadStates.OverCapacity,
            Assert.Single(scenario.Candidate.Members).State);
        Assert.Single((await GetAsync<CapacityPlanResponse>(
            client,
            $"/api/capacity-plans/{plan.Id}")).Allocations);

        using (var sharing = new HttpRequestMessage(
            HttpMethod.Put,
            $"/api/capacity-plans/{plan.Id}/sharing")
        {
            Content = JsonContent.Create(new ShareCapacityPlanRequest([viewer.User.Id]))
        })
        {
            sharing.Headers.IfMatch.Add(new EntityTagHeaderValue("\"1\""));
            var shared = await client.SendAsync(sharing);
            shared.EnsureSuccessStatusCode();
            Assert.Equal("\"2\"", shared.Headers.ETag?.Tag);
        }

        using (var stale = new HttpRequestMessage(
            HttpMethod.Put,
            $"/api/capacity-plans/{plan.Id}")
        {
            Content = JsonContent.Create(definition with { Name = "Stale edit" })
        })
        {
            stale.Headers.IfMatch.Add(new EntityTagHeaderValue("\"1\""));
            await AssertErrorAsync(
                await client.SendAsync(stale),
                HttpStatusCode.Conflict,
                "CONCURRENCY_CONFLICT");
        }

        Authorize(client, viewer);
        var viewerPlan = await GetAsync<CapacityPlanResponse>(
            client,
            $"/api/capacity-plans/{plan.Id}");
        Assert.False(viewerPlan.CanEdit);
        Assert.Equal(
            CapacitySnapshotStatuses.Ready,
            (await GetAsync<CapacitySnapshotResponse>(
                client,
                $"/api/capacity-plans/{plan.Id}/snapshot")).SourceStatus);
        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await client.PostAsJsonAsync(
                $"/api/capacity-plans/{plan.Id}/scenarios",
                new CapacityScenarioRequest(definition.Allocations))).StatusCode);
        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await client.PutAsJsonAsync(
                $"/api/capacity-plans/{plan.Id}",
                definition with { Name = "Viewer edit" })).StatusCode);

        Authorize(client, outsider);
        await AssertErrorAsync(
            await client.GetAsync($"/api/capacity-plans/{plan.Id}"),
            HttpStatusCode.NotFound,
            "CAPACITY_PLAN_NOT_FOUND");

        client.DefaultRequestHeaders.Authorization = null;
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await client.GetAsync("/api/capacity-plans")).StatusCode);

        Authorize(client, owner);
        var archiveResponse = await client.DeleteAsync($"/api/capacity-plans/{plan.Id}");
        archiveResponse.EnsureSuccessStatusCode();
        var ownerList = await GetAsync<CapacityPlanPageResponse>(
            client,
            "/api/capacity-plans?page=1&pageSize=100");
        Assert.Empty(ownerList.Items);
    }

    private static SaveCapacityPlanRequest Definition(
        string ownerUserId,
        string firstProjectId,
        string secondProjectId,
        IReadOnlyCollection<string> viewers) =>
        new(
            "Quarterly staffing",
            "Synthetic capacity plan",
            new DateOnly(2026, 8, 3),
            new DateOnly(2026, 8, 16),
            null,
            [firstProjectId, secondProjectId],
            [new CapacityMemberRequest(ownerUserId, null, 40)],
            [
                new CapacityAllocationRequest(
                    null,
                    ownerUserId,
                    firstProjectId,
                    new DateOnly(2026, 8, 3),
                    new DateOnly(2026, 8, 16),
                    60)
            ],
            viewers);

    private static Task<ProjectResponse> CreateProjectAsync(
        HttpClient client,
        string organizationId,
        string ownerUserId,
        string key) =>
        PostAsync<ProjectResponse>(
            client,
            "/api/projects",
            new CreateProjectRequest(organizationId, key, "Project " + key, ownerUserId));

    private static Task<ProjectResponse> AddViewerAsync(
        HttpClient client,
        string projectId,
        string viewerUserId) =>
        PostAsync<ProjectResponse>(
            client,
            $"/api/projects/{projectId}/members",
            new AddProjectMemberRequest(viewerUserId, ProjectRoles.Viewer));

    private static Task<AuthResponse> RegisterAsync(
        HttpClient client,
        string username,
        string organizationId) =>
        PostAsync<AuthResponse>(
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

    private static async Task<T> GetAsync<T>(HttpClient client, string url)
    {
        var response = await client.GetAsync(url);
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
