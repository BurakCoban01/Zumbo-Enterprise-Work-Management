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

public sealed class PortfolioApiTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> factory;

    public PortfolioApiTests(WebApplicationFactory<Program> factory)
    {
        this.factory = factory;
    }

    [Fact]
    public async Task HierarchyHealthRoadmapSharingIsolationAndStaleConflictAreAuthoritative()
    {
        using var client = factory.CreateClient();
        var stamp = Guid.NewGuid().ToString("N");
        var organizationId = "portfolio-" + stamp;
        var owner = await RegisterAsync(client, "portfolio-owner-" + stamp, organizationId);
        var viewer = await RegisterAsync(client, "portfolio-viewer-" + stamp, organizationId);
        var outsider = await RegisterAsync(client, "portfolio-outsider-" + stamp, "foreign-" + stamp);
        Authorize(client, owner);
        await PostAsync<OrganizationResponse>(
            client,
            "/api/organizations",
            new CreateOrganizationRequest("Portfolio organization", organizationId));
        var firstProject = await CreateProjectAsync(client, organizationId, owner.User.Id, "PA" + stamp[..6]);
        var secondProject = await CreateProjectAsync(client, organizationId, owner.User.Id, "PB" + stamp[..6]);
        await PostAsync<ProjectResponse>(
            client,
            $"/api/projects/{firstProject.Id}/members",
            new AddProjectMemberRequest(viewer.User.Id, ProjectRoles.Viewer));
        await PostAsync<ProjectResponse>(
            client,
            $"/api/projects/{secondProject.Id}/members",
            new AddProjectMemberRequest(viewer.User.Id, ProjectRoles.Viewer));

        var createResponse = await client.PostAsJsonAsync(
            "/api/portfolios",
            new SavePortfolioRequest(
                "Delivery portfolio",
                "Synthetic portfolio",
                [viewer.User.Id]));
        createResponse.EnsureSuccessStatusCode();
        Assert.Equal("\"1\"", createResponse.Headers.ETag?.Tag);
        var portfolio = (await createResponse.Content.ReadFromJsonAsync<
            ApiResponse<PortfolioResponse>>())!.Data!;

        portfolio = await PostAsync<PortfolioResponse>(
            client,
            $"/api/portfolios/{portfolio.Id}/initiatives",
            Initiative("Platform", null, owner.User.Id, [firstProject.Id]));
        var parent = Assert.Single(portfolio.Initiatives);
        portfolio = await PostAsync<PortfolioResponse>(
            client,
            $"/api/portfolios/{portfolio.Id}/initiatives",
            Initiative("Mobile", parent.Id, owner.User.Id, [secondProject.Id]));
        var child = portfolio.Initiatives.Single(item => item.Name == "Mobile");
        portfolio = await PostAsync<PortfolioResponse>(
            client,
            $"/api/portfolios/{portfolio.Id}/initiatives/{child.Id}/status-updates",
            new AddInitiativeStatusUpdateRequest(
                InitiativeStatuses.Active,
                InitiativeHealth.AtRisk,
                60,
                "Milestone dependency is under review."));
        Assert.Equal(InitiativeHealth.AtRisk, portfolio.Initiatives
            .Single(item => item.Id == child.Id).Health);
        Assert.Equal(
            ProjectHistoryRetentionPolicy.MaximumInitiativeStatusUpdates,
            portfolio.Initiatives.Single(item => item.Id == child.Id).StatusUpdateRetentionLimit);

        portfolio = await PostAsync<PortfolioResponse>(
            client,
            $"/api/portfolios/{portfolio.Id}/dependencies",
            new SavePortfolioDependencyRequest(
                firstProject.Id,
                secondProject.Id,
                "Platform delivery enables mobile rollout.",
                PortfolioDependencyStatuses.Active,
                new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero)));
        Assert.Single(portfolio.Dependencies);

        var roadmap = await GetAsync<PortfolioRoadmapResponse>(
            client,
            $"/api/portfolios/{portfolio.Id}/roadmap");
        Assert.Equal(PortfolioSourceStatuses.Ready, roadmap.SourceStatus);
        Assert.Equal(2, roadmap.Initiatives.Count);

        using (var stale = new HttpRequestMessage(
            HttpMethod.Put,
            $"/api/portfolios/{portfolio.Id}")
        {
            Content = JsonContent.Create(new SavePortfolioRequest(
                "Stale portfolio",
                null,
                [viewer.User.Id]))
        })
        {
            stale.Headers.IfMatch.Add(new EntityTagHeaderValue("\"1\""));
            await AssertErrorAsync(
                await client.SendAsync(stale),
                HttpStatusCode.Conflict,
                "CONCURRENCY_CONFLICT");
        }

        Authorize(client, viewer);
        var viewerPortfolio = await GetAsync<PortfolioResponse>(
            client,
            $"/api/portfolios/{portfolio.Id}");
        Assert.False(viewerPortfolio.CanEdit);
        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await client.PutAsJsonAsync(
                $"/api/portfolios/{portfolio.Id}",
                new SavePortfolioRequest("Viewer edit", null, []))).StatusCode);

        Authorize(client, outsider);
        await AssertErrorAsync(
            await client.GetAsync($"/api/portfolios/{portfolio.Id}"),
            HttpStatusCode.NotFound,
            "PORTFOLIO_NOT_FOUND");

        client.DefaultRequestHeaders.Authorization = null;
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await client.GetAsync("/api/portfolios")).StatusCode);

        Authorize(client, owner);
        var archiveResponse = await client.DeleteAsync($"/api/portfolios/{portfolio.Id}");
        archiveResponse.EnsureSuccessStatusCode();
        var ownerList = await GetAsync<PortfolioPageResponse>(
            client,
            "/api/portfolios?page=1&pageSize=100");
        Assert.Empty(ownerList.Items);
    }

    private static SaveInitiativeRequest Initiative(
        string name,
        string? parentId,
        string ownerUserId,
        IReadOnlyCollection<string> projectIds) =>
        new(
            name,
            null,
            parentId,
            ownerUserId,
            InitiativeStatuses.Active,
            InitiativeHealth.OnTrack,
            80,
            new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero),
            projectIds,
            []);

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
