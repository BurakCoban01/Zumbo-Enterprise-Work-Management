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
using Zumbo.Api.Presentation.Controllers.Organizations;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.Modules.Identity;
using Zumbo.SharedKernel;

namespace Zumbo.ApiTests;

public sealed class OrganizationControllerContractTests(WebApplicationFactory<Program> baseFactory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    [Fact]
    public async Task OrganizationRoutes_AreControllerOwnedAndPreservePresentationContracts()
    {
        using var factory = baseFactory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(
                new Dictionary<string, string?> { ["RegistrationProvisioning:Mode"] = "LocalDemo" }));
        });
        using var client = factory.CreateClient();
        var controllerTypes = new[] { typeof(OrganizationCatalogController), typeof(OrganizationLifecycleController), typeof(OrganizationDepartmentsController) };
        var endpoints = factory.Services.GetServices<EndpointDataSource>().SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>().Where(endpoint => controllerTypes.Contains(endpoint.Metadata.GetMetadata<ControllerActionDescriptor>()?.ControllerTypeInfo.AsType())).ToList();
        Assert.Equal(14, endpoints.Count);

        AssertEndpoint(endpoints, HttpMethods.Get, "api/organizations", PermissionCatalog.OrganizationView);
        AssertEndpoint(endpoints, HttpMethods.Post, "api/organizations", PermissionCatalog.OrganizationManage);
        AssertEndpoint(endpoints, HttpMethods.Put, "api/organizations/{organizationId}", PermissionCatalog.OrganizationManage);
        AssertEndpoint(endpoints, HttpMethods.Post, "api/organizations/{organizationId}/ownership-transfer", PermissionCatalog.OrganizationManage);
        AssertEndpoint(endpoints, HttpMethods.Post, "api/organizations/{organizationId}/suspend", PermissionCatalog.OrganizationManage);
        AssertEndpoint(endpoints, HttpMethods.Post, "api/organizations/{organizationId}/archive", PermissionCatalog.OrganizationManage);
        AssertEndpoint(endpoints, HttpMethods.Post, "api/organizations/{organizationId}/restore", PermissionCatalog.OrganizationManage);
        AssertEndpoint(endpoints, HttpMethods.Get, "api/organizations/{organizationId}/members", PermissionCatalog.OrganizationView);
        AssertEndpoint(endpoints, HttpMethods.Post, "api/organizations/{organizationId}/departments", PermissionCatalog.OrganizationManage);
        AssertEndpoint(endpoints, HttpMethods.Put, "api/organizations/{organizationId}/departments/{departmentId}", PermissionCatalog.OrganizationManage);
        AssertEndpoint(endpoints, HttpMethods.Delete, "api/organizations/{organizationId}/departments/{departmentId}", PermissionCatalog.OrganizationManage);
        AssertEndpoint(endpoints, HttpMethods.Post, "api/organizations/{organizationId}/departments/{departmentId}/members", PermissionCatalog.OrganizationManage);
        AssertEndpoint(endpoints, HttpMethods.Patch, "api/organizations/{organizationId}/departments/{departmentId}/members/{userId}", PermissionCatalog.OrganizationManage);
        AssertEndpoint(endpoints, HttpMethods.Delete, "api/organizations/{organizationId}/departments/{departmentId}/members/{userId}", PermissionCatalog.OrganizationManage);

        using var anonymous = await client.GetAsync("/api/organizations");
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        var auth = await RegisterAsync(client);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);
        using var missing = await client.PostAsync("/api/organizations", null);
        Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
        Assert.Equal(0, missing.Content.Headers.ContentLength);
        using var malformed = await client.PostAsync("/api/organizations", new StringContent("{", Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.BadRequest, malformed.StatusCode);
        Assert.Equal(0, malformed.Content.Headers.ContentLength);
    }

    private static void AssertEndpoint(IReadOnlyList<RouteEndpoint> endpoints, string method, string route, string permissionKey)
    {
        var endpoint = Assert.Single(endpoints, candidate => string.Equals(candidate.RoutePattern.RawText?.TrimStart('/'), route, StringComparison.Ordinal)
            && candidate.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods.Contains(method, StringComparer.Ordinal) == true);
        Assert.IsType<ControllerActionDescriptor>(endpoint.Metadata.GetMetadata<ControllerActionDescriptor>());
        Assert.NotNull(endpoint.Metadata.GetMetadata<IAuthorizeData>());
        var permission = Assert.IsAssignableFrom<IEndpointPermissionMetadata>(endpoint.Metadata.GetMetadata<IEndpointPermissionMetadata>());
        Assert.Equal(permissionKey, permission.Permission);
        Assert.False(permission.IsGlobal);
        Assert.Equal("api", endpoint.Metadata.GetMetadata<EnableRateLimitingAttribute>()?.PolicyName);
    }

    private static async Task<AuthResponse> RegisterAsync(HttpClient client)
    {
        var stamp = Guid.NewGuid().ToString("N");
        using var response = await client.PostAsJsonAsync("/api/auth/register", new RegisterUserRequest(
            "organization-contract-" + stamp, $"organization-contract-{stamp}@zumbo.local", "P@ssword123", "organization-contract-org-" + stamp));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ApiResponse<AuthResponse>>())!.Data!;
    }
}
