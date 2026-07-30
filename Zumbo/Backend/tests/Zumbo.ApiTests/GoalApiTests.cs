using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Zumbo.Modules.Identity;
using Zumbo.Modules.Organizations;
using Zumbo.Modules.Projects;
using Zumbo.SharedKernel;

namespace Zumbo.ApiTests;

public sealed class GoalApiTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> factory;

    public GoalApiTests(WebApplicationFactory<Program> factory)
    {
        this.factory = factory;
    }

    [Fact]
    public async Task GoalProgressHistoryLinksAuthorizationAndArchiveAreAuthoritative()
    {
        using var client = factory.CreateClient();
        var stamp = Guid.NewGuid().ToString("N");
        var organizationId = "goal-" + stamp;
        var owner = await RegisterAsync(client, "goal-owner-" + stamp, organizationId);
        var keyResultOwner = await RegisterAsync(
            client,
            "goal-result-" + stamp,
            organizationId);
        var outsider = await RegisterAsync(
            client,
            "goal-outsider-" + stamp,
            "foreign-" + stamp);
        Authorize(client, owner);
        await PostAsync<OrganizationResponse>(
            client,
            "/api/organizations",
            new CreateOrganizationRequest("Goal organization", organizationId));
        var project = await CreateProjectAsync(
            client,
            organizationId,
            owner.User.Id,
            "GK" + stamp[..6]);
        await PostAsync<ProjectResponse>(
            client,
            $"/api/projects/{project.Id}/members",
            new AddProjectMemberRequest(keyResultOwner.User.Id, ProjectRoles.Viewer));
        var portfolio = await PostAsync<PortfolioResponse>(
            client,
            "/api/portfolios",
            new SavePortfolioRequest(
                "Goal portfolio",
                "Synthetic portfolio",
                [keyResultOwner.User.Id]));
        portfolio = await PostAsync<PortfolioResponse>(
            client,
            $"/api/portfolios/{portfolio.Id}/initiatives",
            new SaveInitiativeRequest(
                "Activation",
                null,
                null,
                owner.User.Id,
                InitiativeStatuses.Active,
                InitiativeHealth.OnTrack,
                80,
                new DateTimeOffset(2026, 9, 30, 0, 0, 0, TimeSpan.Zero),
                [project.Id],
                []));
        var initiative = Assert.Single(portfolio.Initiatives);

        var createResponse = await client.PostAsJsonAsync(
            "/api/goals",
            new SaveGoalRequest(
                "Increase activation",
                "Synthetic quarterly objective",
                new DateOnly(2026, 7, 1),
                new DateOnly(2026, 9, 30),
                [keyResultOwner.User.Id],
                [new GoalInitiativeLinkRequest(portfolio.Id, initiative.Id)],
                [project.Id]));
        createResponse.EnsureSuccessStatusCode();
        Assert.Equal("\"1\"", createResponse.Headers.ETag?.Tag);
        var goal = (await createResponse.Content.ReadFromJsonAsync<
            ApiResponse<GoalResponse>>())!.Data!;

        goal = await PostAsync<GoalResponse>(
            client,
            $"/api/goals/{goal.Id}/key-results",
            new SaveKeyResultRequest(
                "Activated teams",
                null,
                keyResultOwner.User.Id,
                0,
                100,
                10,
                "%",
                KeyResultDirections.Increase));
        var keyResult = Assert.Single(goal.KeyResults);
        goal = await PostAsync<GoalResponse>(
            client,
            $"/api/goals/{goal.Id}/status-updates",
            new AddGoalStatusUpdateRequest(
                GoalStatuses.Active,
                GoalHealth.OnTrack,
                75,
                "Activation is on track."));
        Assert.Equal(GoalStatuses.Active, goal.Status);
        Assert.Single(goal.StatusUpdates);
        Assert.Equal(
            ProjectHistoryRetentionPolicy.MaximumGoalStatusUpdates,
            goal.StatusUpdateRetentionLimit);

        using (var stale = new HttpRequestMessage(HttpMethod.Put, $"/api/goals/{goal.Id}")
        {
            Content = JsonContent.Create(new SaveGoalRequest(
                "Stale goal",
                null,
                new DateOnly(2026, 7, 1),
                new DateOnly(2026, 9, 30),
                [keyResultOwner.User.Id],
                [new GoalInitiativeLinkRequest(portfolio.Id, initiative.Id)],
                [project.Id]))
        })
        {
            stale.Headers.IfMatch.Add(new EntityTagHeaderValue("\"1\""));
            await AssertErrorAsync(
                await client.SendAsync(stale),
                HttpStatusCode.Conflict,
                "CONCURRENCY_CONFLICT");
        }

        Authorize(client, keyResultOwner);
        var visible = await GetAsync<GoalResponse>(client, $"/api/goals/{goal.Id}");
        Assert.False(visible.CanEdit);
        Assert.True(Assert.Single(visible.KeyResults).CanUpdate);
        goal = await PostAsync<GoalResponse>(
            client,
            $"/api/goals/{goal.Id}/key-results/{keyResult.Id}/progress-updates",
            new AddKeyResultProgressRequest(
                45,
                70,
                "Activated teams reached forty-five percent."));
        Assert.Equal(45, Assert.Single(goal.KeyResults).Progress);
        Assert.Single(Assert.Single(goal.KeyResults).ProgressUpdates);
        Assert.Equal(
            ProjectHistoryRetentionPolicy.MaximumKeyResultProgressUpdates,
            Assert.Single(goal.KeyResults).ProgressUpdateRetentionLimit);
        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await client.PutAsJsonAsync(
                $"/api/goals/{goal.Id}",
                new SaveGoalRequest(
                    "Forbidden edit",
                    null,
                    new DateOnly(2026, 7, 1),
                    new DateOnly(2026, 9, 30),
                    [],
                    [],
                    []))).StatusCode);

        var rollup = await GetAsync<GoalRollupResponse>(
            client,
            $"/api/goals/{goal.Id}/rollup");
        Assert.Equal(GoalSourceStatuses.Ready, rollup.SourceStatus);
        Assert.Equal(45, rollup.Progress);
        Assert.Single(rollup.Initiatives);
        Assert.Single(rollup.Projects);

        Authorize(client, outsider);
        await AssertErrorAsync(
            await client.GetAsync($"/api/goals/{goal.Id}"),
            HttpStatusCode.NotFound,
            "GOAL_NOT_FOUND");
        client.DefaultRequestHeaders.Authorization = null;
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/goals")).StatusCode);

        Authorize(client, owner);
        var archive = await client.DeleteAsync($"/api/goals/{goal.Id}");
        archive.EnsureSuccessStatusCode();
        var ownerList = await GetAsync<GoalPageResponse>(
            client,
            "/api/goals?page=1&pageSize=100");
        Assert.Empty(ownerList.Items);
    }

    private static Task<ProjectResponse> CreateProjectAsync(
        HttpClient client,
        string organizationId,
        string ownerUserId,
        string key) =>
        PostAsync<ProjectResponse>(
            client,
            "/api/projects",
            new CreateProjectRequest(
                organizationId,
                key,
                "Project " + key,
                ownerUserId));

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
