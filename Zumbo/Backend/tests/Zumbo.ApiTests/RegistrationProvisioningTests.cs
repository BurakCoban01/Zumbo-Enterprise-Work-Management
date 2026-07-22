using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Infrastructure.Persistence;
using Zumbo.BuildingBlocks.Infrastructure.Runtime;
using Zumbo.Modules.Identity;
using Zumbo.Modules.Organizations;
using Zumbo.Modules.Teams;
using Zumbo.SharedKernel;

namespace Zumbo.ApiTests;

public sealed class RegistrationProvisioningTests(WebApplicationFactory<Program> baseFactory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    [Fact]
    public async Task ProductionLike_RequiresBootstrapOrExistingOrganizationInvite()
    {
        using var factory = ProductionLikeFactory("bootstrap-one@zumbo.local", "bootstrap-two@zumbo.local");
        using var client = factory.CreateClient();
        var stamp = Guid.NewGuid().ToString("N");
        var organizationId = "provisioning-" + stamp;

        var arbitrary = await client.PostAsJsonAsync("/api/auth/register", new RegisterUserRequest(
            "arbitrary-" + stamp,
            $"arbitrary-{stamp}@zumbo.local",
            "P@ssword123",
            organizationId));
        await AssertErrorAsync(arbitrary, HttpStatusCode.NotFound, "REGISTRATION_ORGANIZATION_NOT_FOUND");

        var bootstrap = await RegisterAsync(
            client,
            new RegisterUserRequest(
                "bootstrap-one-" + stamp,
                "bootstrap-one@zumbo.local",
                "P@ssword123",
                organizationId,
                "one-time-bootstrap-token"));
        Assert.Contains("SystemAdmin", bootstrap.User.Roles);

        var repeatedBootstrap = await client.PostAsJsonAsync("/api/auth/register", new RegisterUserRequest(
            "bootstrap-two-" + stamp,
            "bootstrap-two@zumbo.local",
            "P@ssword123",
            organizationId,
            "one-time-bootstrap-token"));
        await AssertErrorAsync(repeatedBootstrap, HttpStatusCode.Conflict, "BOOTSTRAP_ALREADY_COMPLETED");

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", bootstrap.AccessToken);
        var organizationResponse = await client.PostAsJsonAsync(
            "/api/organizations",
            new CreateOrganizationRequest("Provisioning Organization", organizationId));
        Assert.Equal(HttpStatusCode.Created, organizationResponse.StatusCode);

        var teamResponse = await client.PostAsJsonAsync(
            "/api/teams",
            new CreateTeamRequest(organizationId, "Provisioning Team", bootstrap.User.Id));
        var team = await AssertSuccessAsync<TeamResponse>(teamResponse, HttpStatusCode.Created);

        var invitedEmail = $"invited-{stamp}@zumbo.local";
        var inviteResponse = await client.PostAsJsonAsync(
            $"/api/teams/{team.Id}/members",
            new InviteTeamMemberRequest(invitedEmail, "Member"));
        Assert.Equal(HttpStatusCode.OK, inviteResponse.StatusCode);

        client.DefaultRequestHeaders.Authorization = null;
        var uninvited = await client.PostAsJsonAsync("/api/auth/register", new RegisterUserRequest(
            "uninvited-" + stamp,
            $"uninvited-{stamp}@zumbo.local",
            "P@ssword123",
            organizationId));
        await AssertErrorAsync(uninvited, HttpStatusCode.Forbidden, "FORBIDDEN");

        var invited = await RegisterAsync(client, new RegisterUserRequest(
            "invited-" + stamp,
            invitedEmail,
            "P@ssword123",
            organizationId.ToUpperInvariant()));
        Assert.DoesNotContain("SystemAdmin", invited.User.Roles);
        Assert.Equal(organizationId, invited.User.OrganizationId);
    }

    [Fact]
    public async Task ProductionLike_ConcurrentBootstrap_AllowsExactlyOneAdministrator()
    {
        using var factory = ProductionLikeFactory("race-one@zumbo.local", "race-two@zumbo.local");
        using var client = factory.CreateClient();
        var organizationId = "bootstrap-race-" + Guid.NewGuid().ToString("N");

        var requests = new[]
        {
            new RegisterUserRequest("race-one", "race-one@zumbo.local", "P@ssword123", organizationId, "one-time-bootstrap-token"),
            new RegisterUserRequest("race-two", "race-two@zumbo.local", "P@ssword123", organizationId, "one-time-bootstrap-token")
        };
        var responses = await Task.WhenAll(requests.Select(request =>
            client.PostAsJsonAsync("/api/auth/register", request)));

        Assert.Single(responses, response => response.StatusCode == HttpStatusCode.OK);
        var rejected = Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Conflict);
        await AssertErrorAsync(rejected, HttpStatusCode.Conflict, "BOOTSTRAP_ALREADY_COMPLETED");
    }

    [Fact]
    public async Task LocalDemo_IsRejectedOutsideDevelopment()
    {
        var policy = new RegistrationProvisioningPolicyAdapter(
            new InMemoryDocumentRepository<OrganizationDocument>(),
            new InMemoryDocumentRepository<TeamDocument>(),
            Options.Create(new RegistrationProvisioningOptions
            {
                Mode = RegistrationProvisioningModes.LocalDemo
            }),
            new TestHostEnvironment { EnvironmentName = "Production" },
            new SystemClock());

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            policy.EnsureAllowedAsync(
                new RegistrationProvisioningRequest("user@zumbo.local", "org", false),
                CancellationToken.None));
        Assert.Equal(
            "RegistrationProvisioning:Mode=LocalDemo is allowed only in Development.",
            exception.Message);
    }

    private WebApplicationFactory<Program> ProductionLikeFactory(params string[] adminEmails) =>
        baseFactory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.ConfigureAppConfiguration((_, configuration) =>
            {
                var values = new Dictionary<string, string?>
                {
                    ["RegistrationProvisioning:Mode"] = RegistrationProvisioningModes.ProductionLike,
                    ["IdentityBootstrap:BootstrapToken"] = "one-time-bootstrap-token"
                };
                for (var index = 0; index < adminEmails.Length; index++)
                {
                    values[$"IdentityBootstrap:AdminEmails:{index}"] = adminEmails[index];
                }

                configuration.AddInMemoryCollection(values);
            });
        });

    private static async Task<AuthResponse> RegisterAsync(HttpClient client, RegisterUserRequest request) =>
        await AssertSuccessAsync<AuthResponse>(
            await client.PostAsJsonAsync("/api/auth/register", request),
            HttpStatusCode.OK);

    private static async Task<T> AssertSuccessAsync<T>(HttpResponseMessage response, HttpStatusCode statusCode)
    {
        Assert.Equal(statusCode, response.StatusCode);
        var envelope = await response.Content.ReadFromJsonAsync<ApiResponse<T>>();
        Assert.NotNull(envelope);
        Assert.True(envelope.Success);
        Assert.NotNull(envelope.Data);
        return envelope.Data!;
    }

    private static async Task AssertErrorAsync(
        HttpResponseMessage response,
        HttpStatusCode statusCode,
        string errorCode)
    {
        Assert.Equal(statusCode, response.StatusCode);
        var envelope = await response.Content.ReadFromJsonAsync<ApiResponse<object>>();
        Assert.NotNull(envelope?.Error);
        Assert.Equal(errorCode, envelope.Error.Code);
    }

    private sealed class TestHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Production";
        public string ApplicationName { get; set; } = "Zumbo.ApiTests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
