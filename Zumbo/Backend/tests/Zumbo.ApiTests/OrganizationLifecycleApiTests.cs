using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Zumbo.Modules.Audit;
using Zumbo.Modules.Identity;
using Zumbo.Modules.Organizations;
using Zumbo.SharedKernel;

namespace Zumbo.ApiTests;

public sealed class OrganizationLifecycleApiTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient client;

    public OrganizationLifecycleApiTests(WebApplicationFactory<Program> factory)
    {
        client = factory.CreateClient();
    }

    [Fact]
    public async Task Lifecycle_UsesETagOwnerTransferRetentionPaginationAndAudit()
    {
        var stamp = Guid.NewGuid().ToString("N");
        var tenantKey = "org-domain001-" + stamp;
        var owner = await PostAsync<AuthResponse>("/api/auth/register", new RegisterUserRequest(
            "domain-owner-" + stamp,
            $"domain-owner-{stamp}@zumbo.local",
            "P@ssword123",
            tenantKey));
        var nextOwner = await PostAsync<AuthResponse>("/api/auth/register", new RegisterUserRequest(
            "domain-next-owner-" + stamp,
            $"domain-next-owner-{stamp}@zumbo.local",
            "P@ssword123",
            tenantKey));
        var memberTwo = await PostAsync<AuthResponse>("/api/auth/register", new RegisterUserRequest(
            "domain-member-two-" + stamp,
            $"domain-member-two-{stamp}@zumbo.local",
            "P@ssword123",
            tenantKey));
        var memberThree = await PostAsync<AuthResponse>("/api/auth/register", new RegisterUserRequest(
            "domain-member-three-" + stamp,
            $"domain-member-three-{stamp}@zumbo.local",
            "P@ssword123",
            tenantKey));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", owner.AccessToken);

        var createResponse = await client.PostAsJsonAsync(
            "/api/organizations",
            new CreateOrganizationRequest("Domain 001", tenantKey));
        createResponse.EnsureSuccessStatusCode();
        var organization = (await createResponse.Content.ReadFromJsonAsync<ApiResponse<OrganizationResponse>>())!.Data!;
        Assert.Equal(1, organization.Version);
        Assert.Equal("\"1\"", createResponse.Headers.ETag?.Tag);

        using (var immutableRequest = VersionedRequest(
            HttpMethod.Put,
            $"/api/organizations/{organization.Id}",
            new UpdateOrganizationRequest("Domain 001", tenantKey + "-changed"),
            1))
        {
            var immutableResponse = await client.SendAsync(immutableRequest);
            Assert.Equal(HttpStatusCode.Conflict, immutableResponse.StatusCode);
            var immutableBody = await immutableResponse.Content.ReadFromJsonAsync<ApiResponse<object>>();
            Assert.Equal("TENANT_KEY_IMMUTABLE", immutableBody!.Error!.Code);
        }

        organization = await SendVersionedAsync(
            HttpMethod.Put,
            $"/api/organizations/{organization.Id}",
            new UpdateOrganizationRequest("Domain 001 Renamed", tenantKey),
            organization.Version);
        using (var staleRequest = VersionedRequest(
            HttpMethod.Put,
            $"/api/organizations/{organization.Id}",
            new UpdateOrganizationRequest("Stale write", tenantKey),
            1))
        {
            var staleResponse = await client.SendAsync(staleRequest);
            Assert.Equal(HttpStatusCode.Conflict, staleResponse.StatusCode);
            var staleBody = await staleResponse.Content.ReadFromJsonAsync<ApiResponse<object>>();
            Assert.Equal("CONCURRENCY_CONFLICT", staleBody!.Error!.Code);
        }

        organization = await SendVersionedAsync(
            HttpMethod.Post,
            $"/api/organizations/{organization.Id}/departments",
            new CreateDepartmentRequest("Engineering", null),
            organization.Version);
        var departmentId = Assert.Single(organization.Departments).Id;
        foreach (var userId in new[] { nextOwner.User.Id, memberTwo.User.Id, memberThree.User.Id })
        {
            organization = await SendVersionedAsync(
                HttpMethod.Post,
                $"/api/organizations/{organization.Id}/departments/{departmentId}/members",
                new AssignDepartmentMemberRequest(userId, "Engineer"),
                organization.Version);
        }

        var firstPage = await GetAsync<OrganizationMemberPageResponse>(
            $"/api/organizations/{organization.Id}/members?pageSize=2");
        Assert.Equal(2, firstPage.Items.Count);
        Assert.NotNull(firstPage.NextCursor);
        var secondPage = await GetAsync<OrganizationMemberPageResponse>(
            $"/api/organizations/{organization.Id}/members?pageSize=2&afterUserId={firstPage.NextCursor}");
        Assert.Single(secondPage.Items);
        Assert.Null(secondPage.NextCursor);

        organization = await SendVersionedAsync(
            HttpMethod.Post,
            $"/api/organizations/{organization.Id}/ownership-transfer",
            new TransferOrganizationOwnershipRequest(nextOwner.User.Id),
            organization.Version);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", nextOwner.AccessToken);
        organization = await SendVersionedAsync(
            HttpMethod.Post,
            $"/api/organizations/{organization.Id}/suspend",
            new SuspendOrganizationRequest("planned maintenance"),
            organization.Version);
        Assert.Equal(OrganizationStatuses.Suspended, organization.Status);

        using (var inactiveRequest = VersionedRequest(
            HttpMethod.Post,
            $"/api/organizations/{organization.Id}/departments",
            new CreateDepartmentRequest("Blocked", null),
            organization.Version))
        {
            var inactiveResponse = await client.SendAsync(inactiveRequest);
            Assert.Equal(HttpStatusCode.Conflict, inactiveResponse.StatusCode);
            var inactiveBody = await inactiveResponse.Content.ReadFromJsonAsync<ApiResponse<object>>();
            Assert.Equal("ORGANIZATION_NOT_ACTIVE", inactiveBody!.Error!.Code);
        }

        organization = await SendVersionedAsync(
            HttpMethod.Post,
            $"/api/organizations/{organization.Id}/restore",
            null,
            organization.Version);
        organization = await SendVersionedAsync(
            HttpMethod.Post,
            $"/api/organizations/{organization.Id}/archive",
            null,
            organization.Version);
        Assert.Equal(OrganizationStatuses.Archived, organization.Status);
        Assert.NotNull(organization.ArchivedAt);
        Assert.NotNull(organization.RetainUntil);
        Assert.True(organization.RetainUntil > organization.ArchivedAt);

        var audit = await GetAsync<AuditLogPageResponse>(
            $"/api/audit?entityType=Organization&entityId={organization.Id}&page=1&pageSize=50");
        Assert.Contains(audit.Items, entry => entry.Action == "OrganizationOwnershipTransferred");
        Assert.Contains(audit.Items, entry => entry.Action == "OrganizationSuspended");
        Assert.Contains(audit.Items, entry => entry.Action == "OrganizationRestored");
        Assert.Contains(audit.Items, entry => entry.Action == "OrganizationArchived");
    }

    private async Task<T> PostAsync<T>(string url, object request)
    {
        var response = await client.PostAsJsonAsync(url, request);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ApiResponse<T>>())!.Data!;
    }

    private async Task<T> GetAsync<T>(string url)
    {
        var response = await client.GetAsync(url);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ApiResponse<T>>())!.Data!;
    }

    private async Task<OrganizationResponse> SendVersionedAsync(
        HttpMethod method,
        string url,
        object? body,
        long expectedVersion)
    {
        using var request = VersionedRequest(method, url, body, expectedVersion);
        var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        var envelope = await response.Content.ReadFromJsonAsync<ApiResponse<OrganizationResponse>>();
        Assert.NotNull(envelope?.Data);
        Assert.Equal($"\"{envelope!.Data!.Version}\"", response.Headers.ETag?.Tag);
        return envelope.Data;
    }

    private static HttpRequestMessage VersionedRequest(
        HttpMethod method,
        string url,
        object? body,
        long expectedVersion)
    {
        var request = new HttpRequestMessage(method, url)
        {
            Content = body is null ? null : JsonContent.Create(body)
        };
        request.Headers.TryAddWithoutValidation("If-Match", $"\"{expectedVersion}\"");
        return request;
    }
}
