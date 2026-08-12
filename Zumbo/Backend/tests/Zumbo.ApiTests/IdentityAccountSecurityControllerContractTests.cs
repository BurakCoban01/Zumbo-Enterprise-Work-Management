using System.Net;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Zumbo.Api.Presentation.Authorization;
using Zumbo.Api.Presentation.Controllers.Identity;
using Zumbo.BuildingBlocks.Application.Security;

namespace Zumbo.ApiTests;

public sealed class IdentityAccountSecurityControllerContractTests(WebApplicationFactory<Program> baseFactory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    [Fact]
    public async Task AccountSecurityRoutes_AreControllerOwnedAndPreservePresentationContracts()
    {
        using var factory = baseFactory.WithWebHostBuilder(builder => builder.UseEnvironment("Development"));
        using var client = factory.CreateClient();
        var controllerTypes = new[]
        {
            typeof(IdentityAccessController),
            typeof(IdentityAccountLifecycleController),
            typeof(IdentityMfaController),
            typeof(IdentitySessionsController),
            typeof(IdentityApiKeysController),
            typeof(BrowserIdentityController)
        };
        var endpoints = factory.Services.GetServices<EndpointDataSource>()
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .Where(endpoint => controllerTypes.Contains(
                endpoint.Metadata.GetMetadata<ControllerActionDescriptor>()?.ControllerTypeInfo.AsType()))
            .ToList();
        Assert.Equal(23, endpoints.Count);

        var anonymous = new (string Method, string Route, string Rate, string Tag)[]
        {
            (HttpMethods.Post, "api/auth/register", "api", "Identity"),
            (HttpMethods.Post, "api/auth/login", "login", "Identity"),
            (HttpMethods.Post, "api/auth/refresh", "api", "Identity"),
            (HttpMethods.Post, "api/auth/logout", "api", "Identity"),
            (HttpMethods.Post, "api/auth/forgot-password", "password-reset", "Identity"),
            (HttpMethods.Post, "api/auth/reset-password", "password-reset", "Identity"),
            (HttpMethods.Post, "api/browser-auth/register", "api", "BrowserIdentity"),
            (HttpMethods.Post, "api/browser-auth/login", "login", "BrowserIdentity"),
            (HttpMethods.Post, "api/browser-auth/refresh", "api", "BrowserIdentity"),
            (HttpMethods.Post, "api/browser-auth/logout", "api", "BrowserIdentity")
        };
        foreach (var contract in anonymous)
        {
            AssertEndpoint(endpoints, contract.Method, contract.Route, contract.Rate, contract.Tag, permission: null);
        }

        var protectedRoutes = new (string Method, string Route)[]
        {
            (HttpMethods.Post, "api/auth/change-password"),
            (HttpMethods.Post, "api/auth/deactivate"),
            (HttpMethods.Get, "api/auth/mfa"),
            (HttpMethods.Post, "api/auth/mfa/setup"),
            (HttpMethods.Post, "api/auth/mfa/confirm"),
            (HttpMethods.Post, "api/auth/mfa/disable"),
            (HttpMethods.Post, "api/auth/mfa/recovery-codes"),
            (HttpMethods.Get, "api/auth/sessions"),
            (HttpMethods.Delete, "api/auth/sessions/{sessionId}"),
            (HttpMethods.Get, "api/auth/api-keys"),
            (HttpMethods.Post, "api/auth/api-keys"),
            (HttpMethods.Delete, "api/auth/api-keys/{apiKeyId}"),
            (HttpMethods.Get, "api/browser-auth/session")
        };
        foreach (var contract in protectedRoutes)
        {
            var tag = contract.Route.StartsWith("api/browser-auth", StringComparison.Ordinal)
                ? "BrowserIdentity"
                : "Identity";
            AssertEndpoint(endpoints, contract.Method, contract.Route, "api", tag, PermissionCatalog.ProfileRead);
        }

        using var missingIdentityBody = await client.PostAsync("/api/auth/login", content: null);
        Assert.Equal(HttpStatusCode.BadRequest, missingIdentityBody.StatusCode);
        Assert.Equal(0, missingIdentityBody.Content.Headers.ContentLength);
        using var missingBrowserBody = await client.PostAsync("/api/browser-auth/login", content: null);
        Assert.Equal(HttpStatusCode.Forbidden, missingBrowserBody.StatusCode);
    }

    private static void AssertEndpoint(
        IReadOnlyList<RouteEndpoint> endpoints,
        string method,
        string route,
        string rate,
        string tag,
        string? permission)
    {
        var endpoint = Assert.Single(endpoints, candidate =>
            string.Equals(candidate.RoutePattern.RawText?.TrimStart('/'), route, StringComparison.Ordinal)
            && candidate.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods.Contains(method, StringComparer.Ordinal) == true);
        Assert.IsType<ControllerActionDescriptor>(endpoint.Metadata.GetMetadata<ControllerActionDescriptor>());
        Assert.Equal(rate, endpoint.Metadata.GetOrderedMetadata<EnableRateLimitingAttribute>().Last().PolicyName);
        Assert.Contains(tag, endpoint.Metadata.GetOrderedMetadata<ITagsMetadata>().SelectMany(metadata => metadata.Tags));
        var authorization = endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>();
        var permissionMetadata = endpoint.Metadata.GetOrderedMetadata<IEndpointPermissionMetadata>().LastOrDefault();
        if (permission is null)
        {
            Assert.Empty(authorization);
            Assert.Null(permissionMetadata);
            return;
        }

        Assert.NotEmpty(authorization);
        Assert.NotNull(permissionMetadata);
        Assert.Equal(permission, permissionMetadata.Permission);
        Assert.False(permissionMetadata.IsGlobal);
    }
}
