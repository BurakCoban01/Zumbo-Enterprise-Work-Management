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

public sealed class PortfolioControllerContractTests(WebApplicationFactory<Program> baseFactory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    [Fact]
    public async Task PortfolioRoutes_AreControllerOwnedAndPreservePresentationContracts()
    {
        using var factory = baseFactory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(
                new Dictionary<string, string?> { ["RegistrationProvisioning:Mode"] = "LocalDemo" }));
        });
        using var client = factory.CreateClient();
        var controllerTypes = new[] { typeof(PortfolioCatalogController), typeof(PortfolioInitiativesController), typeof(PortfolioDependenciesController) };
        var endpoints = factory.Services.GetServices<EndpointDataSource>().SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>().Where(endpoint => controllerTypes.Contains(endpoint.Metadata.GetMetadata<ControllerActionDescriptor>()?.ControllerTypeInfo.AsType())).ToList();
        Assert.Equal(11, endpoints.Count);

        AssertEndpoint(endpoints, HttpMethods.Get, "api/portfolios");
        AssertEndpoint(endpoints, HttpMethods.Post, "api/portfolios");
        AssertEndpoint(endpoints, HttpMethods.Get, "api/portfolios/{portfolioId}");
        AssertEndpoint(endpoints, HttpMethods.Put, "api/portfolios/{portfolioId}");
        AssertEndpoint(endpoints, HttpMethods.Delete, "api/portfolios/{portfolioId}");
        AssertEndpoint(endpoints, HttpMethods.Post, "api/portfolios/{portfolioId}/initiatives");
        AssertEndpoint(endpoints, HttpMethods.Put, "api/portfolios/{portfolioId}/initiatives/{initiativeId}");
        AssertEndpoint(endpoints, HttpMethods.Post, "api/portfolios/{portfolioId}/initiatives/{initiativeId}/status-updates");
        AssertEndpoint(endpoints, HttpMethods.Post, "api/portfolios/{portfolioId}/dependencies");
        AssertEndpoint(endpoints, HttpMethods.Put, "api/portfolios/{portfolioId}/dependencies/{dependencyId}");
        AssertEndpoint(endpoints, HttpMethods.Get, "api/portfolios/{portfolioId}/roadmap");

        using var anonymous = await client.GetAsync("/api/portfolios");
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        var auth = await RegisterAsync(client);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);
        using var missing = await client.PostAsync("/api/portfolios", null);
        Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
        Assert.Equal(0, missing.Content.Headers.ContentLength);
        using var malformed = await client.PostAsync("/api/portfolios", new StringContent("{", Encoding.UTF8, "application/json"));
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
            "portfolio-contract-" + stamp, $"portfolio-contract-{stamp}@zumbo.local", "P@ssword123", "portfolio-contract-org-" + stamp));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ApiResponse<AuthResponse>>())!.Data!;
    }
}
