using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.Modules.Identity;
using Zumbo.Modules.Organizations;
using Zumbo.Modules.Projects;
using Zumbo.Modules.WorkItems;
using Zumbo.SharedKernel;

namespace Zumbo.ApiTests;

public sealed class DevelopmentIntegrationApiTests(
    WebApplicationFactory<Program> baseFactory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    [Fact]
    public async Task ProviderTargetPolicyFailsClosedAndRequiresExplicitLoopbackOptIn()
    {
        var closed = new DevelopmentProviderTargetPolicy(
            Options.Create(new DevelopmentProviderOptions
            {
                AllowedHosts = ["127.0.0.1", "169.254.169.254"]
            }));
        await Assert.ThrowsAsync<ValidationException>(() =>
            closed.ResolveAsync(
                DevelopmentProviders.GitHub,
                "https://127.0.0.1",
                CancellationToken.None));
        await Assert.ThrowsAsync<ValidationException>(() =>
            closed.ResolveAsync(
                DevelopmentProviders.GitHub,
                "https://169.254.169.254/latest/meta-data",
                CancellationToken.None));
        await Assert.ThrowsAsync<ValidationException>(() =>
            closed.ResolveAsync(
                DevelopmentProviders.GitHub,
                "https://8.8.8.8",
                CancellationToken.None));

        var loopback = new DevelopmentProviderTargetPolicy(
            Options.Create(new DevelopmentProviderOptions
            {
                AllowHttpLoopback = true,
                AllowedHosts = ["127.0.0.1"]
            }));
        var resolved = await loopback.ResolveAsync(
            DevelopmentProviders.GitLab,
            "http://127.0.0.1:58443/api/v4",
            CancellationToken.None);
        Assert.Equal("127.0.0.1", resolved.BaseUri.Host);
        Assert.All(resolved.Addresses, address =>
            Assert.True(IPAddress.IsLoopback(address)));
    }

    [Fact]
    public async Task ManagementLinksAndSignedProviderWebhooksEnforceTheirBoundaries()
    {
        using var factory = baseFactory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("BackgroundJobs:Enabled", "false");
            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(
                    new Dictionary<string, string?>
                    {
                        ["BackgroundJobs:Enabled"] = "false"
                    }));
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IDevelopmentProviderGateway>();
                services.AddSingleton<
                    IDevelopmentProviderGateway,
                    TestProviderGateway>();
            });
        });
        using var client = factory.CreateClient();
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await client.GetAsync(
                "/api/integrations/development")).StatusCode);

        var stamp = Guid.NewGuid().ToString("N");
        var tenant = "development-a-" + stamp;
        var owner = await RegisterAsync(
            client,
            "development-owner-" + stamp,
            tenant);
        Authenticate(client, owner);
        _ = await PostAsync<OrganizationResponse>(
            client,
            "/api/organizations",
            new CreateOrganizationRequest(
                "Development organization",
                tenant),
            HttpStatusCode.Created);
        var project = await PostAsync<ProjectResponse>(
            client,
            "/api/projects",
            new CreateProjectRequest(
                tenant,
                "DV" + stamp[..6],
                "Development project",
                owner.User.Id),
            HttpStatusCode.Created);

        const string accessToken =
            "synthetic-api-provider-token-123456";
        var githubReceipt = await PostAsync<DevelopmentConnectionReceipt>(
            client,
            "/api/integrations/development",
            new CreateDevelopmentConnectionRequest(
                "GitHub",
                DevelopmentProviders.GitHub,
                string.Empty,
                accessToken),
            HttpStatusCode.Created);
        Assert.StartsWith("ghsec_", githubReceipt.WebhookSecret);
        Assert.Equal(
            ["metadata:read", "pull_requests:read", "commit_statuses:read"],
            githubReceipt.Connection.RequiredScopes);

        var listed = await GetAsync<
            IReadOnlyCollection<DevelopmentConnectionResponse>>(
            client,
            "/api/integrations/development");
        Assert.Equal(
            githubReceipt.Connection.Id,
            Assert.Single(listed).Id);
        var connectionBody = await (await client.GetAsync(
                $"/api/integrations/development/{githubReceipt.Connection.Id}"))
            .Content.ReadAsStringAsync();
        Assert.DoesNotContain(
            githubReceipt.WebhookSecret,
            connectionBody,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            accessToken,
            connectionBody,
            StringComparison.Ordinal);

        using (var scope = factory.Services.CreateScope())
        {
            var connections = scope.ServiceProvider.GetRequiredService<
                IDocumentRepository<DevelopmentConnectionDocument>>();
            var stored = await connections.SelectAsync(
                item => item.Id == githubReceipt.Connection.Id);
            Assert.NotNull(stored);
            Assert.DoesNotContain(
                accessToken,
                JsonSerializer.Serialize(stored),
                StringComparison.Ordinal);
        }

        var health = await PostAsync<DevelopmentHealthResponse>(
            client,
            $"/api/integrations/development/{githubReceipt.Connection.Id}/health",
            body: null);
        Assert.Equal("Healthy", health.Status);
        var repositories = await GetAsync<DevelopmentRepositoryPage>(
            client,
            $"/api/integrations/development/{githubReceipt.Connection.Id}/repositories");
        Assert.Equal("Complete", repositories.SourceStatus);
        Assert.Equal("acme/repo", Assert.Single(repositories.Items).FullName);

        var mapping = await PostAsync<DevelopmentRepositoryMappingResponse>(
            client,
            $"/api/integrations/development/{githubReceipt.Connection.Id}/mappings",
            new CreateDevelopmentRepositoryMappingRequest(
                project.Id,
                "42",
                "repo",
                "acme/repo",
                "https://github.com/acme/repo",
                "main"),
            HttpStatusCode.Created);
        using (var scope = factory.Services.CreateScope())
        {
            var workItems = scope.ServiceProvider.GetRequiredService<
                IDocumentRepository<WorkItemDocument>>();
            _ = await workItems.CreateAsync(new WorkItemDocument
            {
                Id = "abcdef12" + stamp[..8],
                ProjectId = project.Id,
                BoardId = "synthetic-board",
                ColumnId = "synthetic-column",
                Title = "Synthetic development work item",
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            });
        }
        var workItemId = "abcdef12" + stamp[..8];
        var workItemMappings = await GetAsync<
            IReadOnlyCollection<DevelopmentRepositoryMappingResponse>>(
            client,
            $"/api/work-items/{workItemId}/development-links/mappings");
        Assert.Equal(mapping.Id, Assert.Single(workItemMappings).Id);
        var manualLink = await PostAsync<WorkItemDevelopmentLinkResponse>(
            client,
            $"/api/work-items/{workItemId}/development-links",
            new CreateWorkItemDevelopmentLinkRequest(
                mapping.Id,
                DevelopmentLinkKinds.PullRequest,
                "pr:16",
                "Manual pull request",
                "https://github.com/acme/repo/pull/16",
                "feature/manual",
                "fedcba9876543210",
                "Open"),
            HttpStatusCode.Created);
        Assert.Equal("Manual", manualLink.Source);
        Assert.Equal(
            manualLink.Id,
            Assert.Single(await GetAsync<
                IReadOnlyCollection<WorkItemDevelopmentLinkResponse>>(
                client,
                $"/api/work-items/{workItemId}/development-links")).Id);
        using (var scope = factory.Services.CreateScope())
        {
            var resolver = scope.ServiceProvider.GetRequiredService<
                Zumbo.Modules.Audit.IAuditTenantResolver>();
            Assert.Equal(
                tenant,
                (await resolver.ResolveAsync(
                    "DevelopmentConnection",
                    githubReceipt.Connection.Id,
                    owner.User.Id,
                    CancellationToken.None)).OrganizationId);
            Assert.Equal(
                tenant,
                (await resolver.ResolveAsync(
                    "DevelopmentRepositoryMapping",
                    mapping.Id,
                    owner.User.Id,
                    CancellationToken.None)).OrganizationId);

            var privacy = scope.ServiceProvider.GetRequiredService<
                IPrivacyDataProcessor>();
            var privacyGroups = await privacy.ExportAsync(
                owner.User.Id,
                tenant,
                CancellationToken.None);
            Assert.Equal(
                githubReceipt.Connection.Id,
                Assert.Single(
                    privacyGroups.Single(
                        group => group.Category == "development-connections")
                    .Items).ResourceId);
        }

        var githubPayload = Encoding.UTF8.GetBytes(
            $$"""
            {
              "number": 17,
              "repository": { "id": 42 },
              "pull_request": {
                "title": "Implement {{project.Key}}-abcdef12",
                "body": "Synthetic signed request",
                "html_url": "https://github.com/acme/repo/pull/17",
                "state": "open",
                "merged": false,
                "updated_at": "2026-07-29T12:00:00Z",
                "head": {
                  "ref": "feature/{{project.Key}}-abcdef12",
                  "sha": "0123456789abcdef"
                }
              }
            }
            """);
        var excessiveReferences = string.Join(
            ' ',
            Enumerable.Range(
                    1,
                    DevelopmentIntegrationLimits.MaximumWorkItemReferencesPerEvent + 1)
                .Select(index => $"{project.Key}-{index:x8}"));
        var excessivePayload = Encoding.UTF8.GetBytes(
            Encoding.UTF8.GetString(githubPayload).Replace(
                $"Implement {project.Key}-abcdef12",
                excessiveReferences,
                StringComparison.Ordinal));
        var excessive = await SendGitHubWebhookAsync(
            client,
            githubReceipt.Connection.Id,
            "github-delivery-reference-limit",
            excessivePayload,
            githubReceipt.WebhookSecret);
        await AssertErrorAsync(
            excessive,
            HttpStatusCode.BadRequest,
            "DEVELOPMENT_WEBHOOK_REFERENCE_LIMIT_EXCEEDED");
        Assert.DoesNotContain(
            $"{project.Key}-0000000b",
            await excessive.Content.ReadAsStringAsync(),
            StringComparison.Ordinal);

        var invalid = await SendGitHubWebhookAsync(
            client,
            githubReceipt.Connection.Id,
            "github-delivery-invalid",
            githubPayload,
            "wrong-secret");
        Assert.Equal(HttpStatusCode.Unauthorized, invalid.StatusCode);
        var accepted = await SendGitHubWebhookAsync(
            client,
            githubReceipt.Connection.Id,
            "github-delivery-1",
            githubPayload,
            githubReceipt.WebhookSecret);
        Assert.Equal(HttpStatusCode.Accepted, accepted.StatusCode);
        var acceptedBody = (await accepted.Content.ReadFromJsonAsync<
            ApiResponse<DevelopmentWebhookResult>>())!.Data!;
        Assert.Equal("Accepted", acceptedBody.Status);
        var duplicate = await SendGitHubWebhookAsync(
            client,
            githubReceipt.Connection.Id,
            "github-delivery-1",
            githubPayload,
            githubReceipt.WebhookSecret);
        Assert.True((await duplicate.Content.ReadFromJsonAsync<
            ApiResponse<DevelopmentWebhookResult>>())!.Data!.Duplicate);

        var changedPayload = Encoding.UTF8.GetBytes(
            Encoding.UTF8.GetString(githubPayload)
                .Replace(
                    "\"state\": \"open\"",
                    "\"state\": \"closed\"",
                    StringComparison.Ordinal));
        var collision = await SendGitHubWebhookAsync(
            client,
            githubReceipt.Connection.Id,
            "github-delivery-1",
            changedPayload,
            githubReceipt.WebhookSecret);
        await AssertErrorAsync(
            collision,
            HttpStatusCode.Conflict,
            "DEVELOPMENT_WEBHOOK_DELIVERY_COLLISION");

        var gitlabReceipt = await PostAsync<DevelopmentConnectionReceipt>(
            client,
            "/api/integrations/development",
            new CreateDevelopmentConnectionRequest(
                "GitLab",
                DevelopmentProviders.GitLab,
                string.Empty,
                accessToken),
            HttpStatusCode.Created);
        var gitlabPayload = Encoding.UTF8.GetBytes(
            """
            {
              "object_kind": "merge_request",
              "project": { "id": 43 },
              "object_attributes": {
                "iid": 3,
                "title": "Synthetic merge request",
                "url": "https://gitlab.com/acme/repo/-/merge_requests/3",
                "state": "opened"
              }
            }
            """);
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
        var gitlabAccepted = await SendGitLabWebhookAsync(
            client,
            gitlabReceipt.Connection.Id,
            "gitlab-delivery-1",
            timestamp,
            gitlabPayload,
            gitlabReceipt.WebhookSecret);
        Assert.Equal(HttpStatusCode.Accepted, gitlabAccepted.StatusCode);

        using (var scope = factory.Services.CreateScope())
        {
            var receipts = scope.ServiceProvider.GetRequiredService<
                IDocumentRepository<DevelopmentWebhookReceiptDocument>>();
            var storedReceipts = await receipts.ListByFilterAsync(
                item => item.OrganizationId == tenant,
                item => item.ReceivedAtUtc,
                pageSize: 20);
            Assert.Equal(2, storedReceipts.Count);
            Assert.All(storedReceipts, receipt =>
            {
                Assert.Equal(64, receipt.PayloadSha256.Length);
                Assert.DoesNotContain(
                    "pull_request",
                    receipt.PayloadSha256,
                    StringComparison.Ordinal);
            });
        }

        var member = await RegisterAsync(
            client,
            "development-member-" + stamp,
            tenant);
        Authenticate(client, member);
        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await client.GetAsync(
                "/api/integrations/development")).StatusCode);
        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await client.GetAsync(
                $"/api/work-items/{workItemId}/development-links/mappings"))
            .StatusCode);

        var foreignTenant = "development-b-" + stamp;
        var foreignAdmin = await RegisterAsync(
            client,
            "development-foreign-" + stamp,
            foreignTenant);
        Authenticate(client, foreignAdmin);
        _ = await PostAsync<OrganizationResponse>(
            client,
            "/api/organizations",
            new CreateOrganizationRequest(
                "Foreign development organization",
                foreignTenant),
            HttpStatusCode.Created);
        await AssertErrorAsync(
            await client.GetAsync(
                $"/api/integrations/development/{githubReceipt.Connection.Id}"),
            HttpStatusCode.NotFound,
            "DEVELOPMENT_CONNECTION_NOT_FOUND");
    }

    private static async Task<HttpResponseMessage> SendGitHubWebhookAsync(
        HttpClient client,
        string connectionId,
        string deliveryId,
        byte[] payload,
        string secret)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"/api/integrations/development/{connectionId}/webhook")
        {
            Content = new ByteArrayContent(payload)
        };
        request.Content.Headers.ContentType =
            new MediaTypeHeaderValue("application/json");
        request.Headers.TryAddWithoutValidation(
            "X-GitHub-Delivery",
            deliveryId);
        request.Headers.TryAddWithoutValidation(
            "X-GitHub-Event",
            "pull_request");
        request.Headers.TryAddWithoutValidation(
            "X-Hub-Signature-256",
            "sha256=" + Convert
                .ToHexString(hmac.ComputeHash(payload))
                .ToLowerInvariant());
        return await client.SendAsync(request);
    }

    private static async Task<HttpResponseMessage> SendGitLabWebhookAsync(
        HttpClient client,
        string connectionId,
        string deliveryId,
        string timestamp,
        byte[] payload,
        string secret)
    {
        var key = Convert.FromBase64String(secret[6..]);
        var prefix = Encoding.UTF8.GetBytes(
            $"{deliveryId}.{timestamp}.");
        var message = new byte[prefix.Length + payload.Length];
        Buffer.BlockCopy(prefix, 0, message, 0, prefix.Length);
        Buffer.BlockCopy(payload, 0, message, prefix.Length, payload.Length);
        using var hmac = new HMACSHA256(key);
        var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"/api/integrations/development/{connectionId}/webhook")
        {
            Content = new ByteArrayContent(payload)
        };
        request.Content.Headers.ContentType =
            new MediaTypeHeaderValue("application/json");
        request.Headers.TryAddWithoutValidation(
            "webhook-id",
            deliveryId);
        request.Headers.TryAddWithoutValidation(
            "webhook-timestamp",
            timestamp);
        request.Headers.TryAddWithoutValidation(
            "webhook-signature",
            "v1," + Convert.ToBase64String(hmac.ComputeHash(message)));
        request.Headers.TryAddWithoutValidation(
            "X-Gitlab-Event",
            "Merge Request Hook");
        return await client.SendAsync(request);
    }

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

    private static void Authenticate(
        HttpClient client,
        AuthResponse authentication) =>
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue(
                "Bearer",
                authentication.AccessToken);

    private static async Task<T> PostAsync<T>(
        HttpClient client,
        string url,
        object? body,
        HttpStatusCode expectedStatus = HttpStatusCode.OK)
    {
        var response = body is null
            ? await client.PostAsync(url, null)
            : await client.PostAsJsonAsync(url, body);
        Assert.Equal(expectedStatus, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<ApiResponse<T>>())!
            .Data!;
    }

    private static async Task<T> GetAsync<T>(
        HttpClient client,
        string url)
    {
        var response = await client.GetAsync(url);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ApiResponse<T>>())!
            .Data!;
    }

    private static async Task AssertErrorAsync(
        HttpResponseMessage response,
        HttpStatusCode status,
        string code)
    {
        Assert.Equal(status, response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<
            ApiResponse<JsonElement>>();
        Assert.Equal(code, error!.Error!.Code);
    }

    private sealed class TestProviderGateway : IDevelopmentProviderGateway
    {
        public Task ValidateBaseUrlAsync(
            string provider,
            string baseUrl,
            CancellationToken ct) => Task.CompletedTask;

        public Task<DevelopmentProviderProbeResult> ProbeAsync(
            string provider,
            string baseUrl,
            string accessToken,
            CancellationToken ct) =>
            Task.FromResult(new DevelopmentProviderProbeResult(true, null));

        public Task<DevelopmentProviderRepositoryResult> ListRepositoriesAsync(
            string provider,
            string baseUrl,
            string accessToken,
            int maximumItems,
            CancellationToken ct) =>
            Task.FromResult(new DevelopmentProviderRepositoryResult(
                [
                    new DevelopmentProviderRepository(
                        "42",
                        "repo",
                        "acme/repo",
                        provider == DevelopmentProviders.GitHub
                            ? "https://github.com/acme/repo"
                            : "https://gitlab.com/acme/repo",
                        "main")
                ],
                false));
    }
}
