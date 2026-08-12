using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Zumbo.Api.Presentation.Authorization;
using Zumbo.Api.Presentation.Controllers.Strategy;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.Modules.Identity;
using Zumbo.SharedKernel;

namespace Zumbo.ApiTests;

public sealed class GoalControllerContractTests(WebApplicationFactory<Program> baseFactory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    [Fact]
    public async Task GoalRoutes_AreControllerOwnedAndPreservePresentationContracts()
    {
        using var factory = baseFactory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(
                new Dictionary<string, string?> { ["RegistrationProvisioning:Mode"] = "LocalDemo" }));
        });
        using var client = factory.CreateClient();
        var controllerTypes = new[] { typeof(GoalCatalogController), typeof(GoalKeyResultsController), typeof(GoalStatusController) };
        var endpoints = factory.Services.GetServices<EndpointDataSource>().SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>().Where(endpoint => controllerTypes.Contains(endpoint.Metadata.GetMetadata<ControllerActionDescriptor>()?.ControllerTypeInfo.AsType())).ToList();
        Assert.Equal(10, endpoints.Count);

        AssertEndpoint(endpoints, HttpMethods.Get, "api/goals");
        AssertEndpoint(endpoints, HttpMethods.Post, "api/goals");
        AssertEndpoint(endpoints, HttpMethods.Get, "api/goals/{goalId}");
        AssertEndpoint(endpoints, HttpMethods.Put, "api/goals/{goalId}");
        AssertEndpoint(endpoints, HttpMethods.Delete, "api/goals/{goalId}");
        AssertEndpoint(endpoints, HttpMethods.Get, "api/goals/{goalId}/rollup");
        AssertEndpoint(endpoints, HttpMethods.Post, "api/goals/{goalId}/key-results");
        AssertEndpoint(endpoints, HttpMethods.Put, "api/goals/{goalId}/key-results/{keyResultId}");
        AssertEndpoint(endpoints, HttpMethods.Post, "api/goals/{goalId}/key-results/{keyResultId}/progress-updates");
        AssertEndpoint(endpoints, HttpMethods.Post, "api/goals/{goalId}/status-updates");

        using var anonymous = await client.GetAsync("/api/goals");
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        var auth = await RegisterAsync(client);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);
        using var missing = await client.PostAsync("/api/goals", null);
        Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
        Assert.Equal(0, missing.Content.Headers.ContentLength);
        using var malformed = await client.PostAsync("/api/goals", new StringContent("{", Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.BadRequest, malformed.StatusCode);
        Assert.Equal(0, malformed.Content.Headers.ContentLength);
    }

    private static void AssertEndpoint(IReadOnlyList<RouteEndpoint> endpoints, string method, string route)
    {
        var endpoint = Assert.Single(endpoints, candidate => string.Equals(candidate.RoutePattern.RawText?.TrimStart('/'), route, StringComparison.Ordinal)
            && candidate.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods.Contains(method, StringComparer.Ordinal) == true);
        Assert.IsType<ControllerActionDescriptor>(endpoint.Metadata.GetMetadata<ControllerActionDescriptor>());
        Assert.NotNull(endpoint.Metadata.GetMetadata<IAuthorizeData>());
        var permission = Assert.IsAssignableFrom<IEndpointPermissionMetadata>(endpoint.Metadata.GetMetadata<IEndpointPermissionMetadata>());
        Assert.Equal(PermissionCatalog.ProjectView, permission.Permission);
        Assert.False(permission.IsGlobal);
        Assert.Equal("api", endpoint.Metadata.GetMetadata<EnableRateLimitingAttribute>()?.PolicyName);
    }

    private static async Task<AuthResponse> RegisterAsync(HttpClient client)
    {
        var stamp = Guid.NewGuid().ToString("N");
        using var response = await client.PostAsJsonAsync("/api/auth/register", new RegisterUserRequest(
            "goal-contract-" + stamp, $"goal-contract-{stamp}@zumbo.local", "P@ssword123", "goal-contract-org-" + stamp));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ApiResponse<AuthResponse>>())!.Data!;
    }
}
