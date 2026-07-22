using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Zumbo.Modules.Identity;
using Zumbo.Modules.Organizations;
using Zumbo.Modules.Projects;
using Zumbo.SharedKernel;

namespace Zumbo.ApiTests;

public sealed class BrowserSessionSecurityTests(WebApplicationFactory<Program> baseFactory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    private const string FrontendOrigin = "https://frontend.test";

    [Fact]
    public async Task BrowserCookies_CsrfOriginRotationAndRevocation_AreEnforced()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://api.test"),
            HandleCookies = true,
            AllowAutoRedirect = false
        });
        client.DefaultRequestHeaders.Add("Origin", FrontendOrigin);
        var stamp = Guid.NewGuid().ToString("N");

        var register = await client.PostAsJsonAsync(
            "/api/browser-auth/register",
            new RegisterUserRequest(
                "browser-" + stamp,
                $"browser-{stamp}@zumbo.local",
                "P@ssword123",
                "browser-org-" + stamp));
        Assert.Equal(HttpStatusCode.OK, register.StatusCode);

        var rawBody = await register.Content.ReadAsStringAsync();
        Assert.DoesNotContain("accessToken", rawBody, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("refreshToken", rawBody, StringComparison.OrdinalIgnoreCase);
        var session = (await register.Content.ReadFromJsonAsync<ApiResponse<BrowserSessionResponse>>())!.Data!;
        Assert.False(string.IsNullOrWhiteSpace(session.CsrfToken));

        var cookies = register.Headers.GetValues("Set-Cookie").ToList();
        AssertCookie(cookies, "zumbo-access", httpOnly: true);
        AssertCookie(cookies, "zumbo-refresh", httpOnly: true);
        AssertCookie(cookies, "zumbo-csrf", httpOnly: false);

        var sessionResponse = await client.GetAsync("/api/browser-auth/session");
        Assert.Equal(HttpStatusCode.OK, sessionResponse.StatusCode);
        session = (await sessionResponse.Content.ReadFromJsonAsync<ApiResponse<BrowserSessionResponse>>())!.Data!;

        using (var createOrganization = new HttpRequestMessage(HttpMethod.Post, "/api/organizations"))
        {
            createOrganization.Headers.Add("X-CSRF-Token", session.CsrfToken);
            createOrganization.Content = JsonContent.Create(new CreateOrganizationRequest(
                "Browser Organization",
                session.User.OrganizationId));
            Assert.Equal(HttpStatusCode.Created, (await client.SendAsync(createOrganization)).StatusCode);
        }

        var projectRequest = new CreateProjectRequest(
            session.User.OrganizationId,
            "B" + stamp[..7],
            "Browser CSRF project",
            session.User.Id);
        var unprotectedMutation = await client.PostAsJsonAsync("/api/projects", projectRequest);
        Assert.Equal(HttpStatusCode.Forbidden, unprotectedMutation.StatusCode);

        using (var protectedMutation = new HttpRequestMessage(HttpMethod.Post, "/api/projects"))
        {
            protectedMutation.Headers.Add("X-CSRF-Token", session.CsrfToken);
            protectedMutation.Content = JsonContent.Create(projectRequest);
            Assert.Equal(HttpStatusCode.Created, (await client.SendAsync(protectedMutation)).StatusCode);
        }

        var noCsrf = await client.PostAsJsonAsync("/api/browser-auth/refresh", new { });
        Assert.Equal(HttpStatusCode.Forbidden, noCsrf.StatusCode);

        using (var wrongOrigin = new HttpRequestMessage(HttpMethod.Post, "/api/browser-auth/refresh"))
        {
            wrongOrigin.Headers.Add("Origin", "https://attacker.test");
            wrongOrigin.Headers.Add("X-CSRF-Token", session.CsrfToken);
            wrongOrigin.Content = JsonContent.Create(new { });
            Assert.Equal(HttpStatusCode.Forbidden, (await client.SendAsync(wrongOrigin)).StatusCode);
        }

        using var refresh = new HttpRequestMessage(HttpMethod.Post, "/api/browser-auth/refresh");
        refresh.Headers.Add("X-CSRF-Token", session.CsrfToken);
        refresh.Content = JsonContent.Create(new { });
        var refreshed = await client.SendAsync(refresh);
        Assert.Equal(HttpStatusCode.OK, refreshed.StatusCode);
        var refreshedBody = await refreshed.Content.ReadAsStringAsync();
        Assert.DoesNotContain("accessToken", refreshedBody, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("refreshToken", refreshedBody, StringComparison.OrdinalIgnoreCase);
        session = (await refreshed.Content.ReadFromJsonAsync<ApiResponse<BrowserSessionResponse>>())!.Data!;
        var rotatedCookies = refreshed.Headers.GetValues("Set-Cookie").ToList();
        var replayRefreshCookie = CookieValue(rotatedCookies, "zumbo-refresh");
        var replayCsrfCookie = CookieValue(rotatedCookies, "zumbo-csrf");

        using var logout = new HttpRequestMessage(HttpMethod.Post, "/api/browser-auth/logout");
        logout.Headers.Add("X-CSRF-Token", session.CsrfToken);
        logout.Content = JsonContent.Create(new BrowserLogoutRequest());
        Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(logout)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/browser-auth/session")).StatusCode);

        using var replayClient = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://api.test"),
            HandleCookies = false
        });
        using var refreshAfterLogout = new HttpRequestMessage(HttpMethod.Post, "/api/browser-auth/refresh");
        refreshAfterLogout.Headers.Add("Origin", FrontendOrigin);
        refreshAfterLogout.Headers.Add("X-CSRF-Token", session.CsrfToken);
        refreshAfterLogout.Headers.Add(
            "Cookie",
            $"zumbo-refresh={replayRefreshCookie}; zumbo-csrf={replayCsrfCookie}");
        refreshAfterLogout.Content = JsonContent.Create(new { });
        Assert.Equal(HttpStatusCode.Unauthorized, (await replayClient.SendAsync(refreshAfterLogout)).StatusCode);
    }

    [Fact]
    public async Task ApprovedBearerClient_RemainsCompatibleWithoutOriginOrCsrf()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();
        var stamp = Guid.NewGuid().ToString("N");
        var registration = await PostAsync<AuthResponse>(client, "/api/auth/register", new RegisterUserRequest(
            "bearer-" + stamp,
            $"bearer-{stamp}@zumbo.local",
            "P@ssword123",
            "bearer-org-" + stamp));

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", registration.AccessToken);
        var setup = await client.PostAsJsonAsync("/api/auth/mfa/setup", new BeginMfaSetupRequest("P@ssword123"));
        Assert.Equal(HttpStatusCode.OK, setup.StatusCode);
    }

    [Fact]
    public async Task BrowserLogin_WithoutOrigin_IsRejected()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync(
            "/api/browser-auth/login",
            new LoginRequest("missing", "bad-password"));
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task BrowserCorsPreflight_AllowsConfiguredCredentialedOrigin()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Options, "/api/browser-auth/session");
        request.Headers.Add("Origin", FrontendOrigin);
        request.Headers.Add("Access-Control-Request-Method", "GET");

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(FrontendOrigin, response.Headers.GetValues("Access-Control-Allow-Origin").Single());
        Assert.Equal("true", response.Headers.GetValues("Access-Control-Allow-Credentials").Single());
    }

    private WebApplicationFactory<Program> CreateFactory() =>
        baseFactory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["AllowedHosts"] = "api.test;localhost",
                    ["Cors:AllowedOrigins:0"] = FrontendOrigin,
                    ["BrowserSession:SecureCookies"] = "true"
                }));
        });

    private static void AssertCookie(IReadOnlyCollection<string> cookies, string name, bool httpOnly)
    {
        var cookie = Assert.Single(cookies, x => x.StartsWith(name + "=", StringComparison.Ordinal));
        Assert.Contains("secure", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("path=/", cookie, StringComparison.OrdinalIgnoreCase);
        if (httpOnly)
        {
            Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        }
        else
        {
            Assert.DoesNotContain("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        }
    }

    private static string CookieValue(IReadOnlyCollection<string> cookies, string name)
    {
        var cookie = Assert.Single(cookies, x => x.StartsWith(name + "=", StringComparison.Ordinal));
        return cookie[(name.Length + 1)..cookie.IndexOf(';')];
    }

    private static async Task<T> PostAsync<T>(HttpClient client, string path, object request)
    {
        var response = await client.PostAsJsonAsync(path, request);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ApiResponse<T>>())!.Data!;
    }
}
