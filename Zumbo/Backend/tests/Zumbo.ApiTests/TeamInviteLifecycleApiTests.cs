using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Zumbo.Modules.Audit;
using Zumbo.Modules.Identity;
using Zumbo.Modules.Notifications;
using Zumbo.Modules.Organizations;
using Zumbo.Modules.Teams;
using Zumbo.SharedKernel;

namespace Zumbo.ApiTests;

public sealed class TeamInviteLifecycleApiTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient client;

    public TeamInviteLifecycleApiTests(WebApplicationFactory<Program> factory)
    {
        client = factory.CreateClient();
    }

    [Fact]
    public async Task InviteLifecycle_UsesOneTimeTokenPaginationOwnershipAndDurableNotification()
    {
        var stamp = Guid.NewGuid().ToString("N");
        var organizationId = "team-domain002-" + stamp;
        var owner = await RegisterAsync("team-owner-" + stamp, organizationId);
        var member = await RegisterAsync("team-member-" + stamp, organizationId);
        var secondMember = await RegisterAsync("team-second-" + stamp, organizationId);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", owner.AccessToken);
        var organization = await PostAsync<OrganizationResponse>(
            "/api/organizations",
            new CreateOrganizationRequest("Team Domain 002", organizationId));
        var team = await PostAsync<TeamResponse>(
            "/api/teams",
            new CreateTeamRequest(organizationId, "Platform", owner.User.Id));

        team = await SendVersionedAsync(
            HttpMethod.Post,
            $"/api/teams/{team.Id}/members",
            new InviteTeamMemberRequest(member.User.Email, TeamRoles.Member),
            team.Version);
        var memberToken = Assert.IsType<string>(team.InvitationToken);
        Assert.DoesNotContain("InvitationTokenHash", await GetRawTeamListAsync(organizationId), StringComparison.Ordinal);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", member.AccessToken);
        var notification = await WaitForInvitationNotificationAsync();
        Assert.Equal("TeamInvitation", notification.Type);

        using var firstAccept = VersionedRequest(
            HttpMethod.Post,
            $"/api/teams/{team.Id}/invites/accept",
            new TeamInviteTokenRequest(memberToken),
            team.Version);
        using var secondAccept = VersionedRequest(
            HttpMethod.Post,
            $"/api/teams/{team.Id}/invites/accept",
            new TeamInviteTokenRequest(memberToken),
            team.Version);
        var acceptResponses = await Task.WhenAll(client.SendAsync(firstAccept), client.SendAsync(secondAccept));
        Assert.Single(acceptResponses, response => response.IsSuccessStatusCode);
        Assert.Single(acceptResponses, response => response.StatusCode == HttpStatusCode.Conflict);
        var acceptedResponse = acceptResponses.Single(response => response.IsSuccessStatusCode);
        team = (await acceptedResponse.Content.ReadFromJsonAsync<ApiResponse<TeamResponse>>())!.Data!;
        Assert.Contains(team.Members, item => item.UserId == member.User.Id && item.Status == TeamMemberStatuses.Active);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", owner.AccessToken);
        team = await SendVersionedAsync(
            HttpMethod.Post,
            $"/api/teams/{team.Id}/members",
            new InviteTeamMemberRequest(secondMember.User.Email, TeamRoles.Member),
            team.Version);
        var revokedToken = Assert.IsType<string>(team.InvitationToken);
        var revokedInviteId = team.Members.Single(item =>
            item.Email == secondMember.User.Email && item.Status == TeamMemberStatuses.Invited).Id;
        team = await SendVersionedAsync(
            HttpMethod.Post,
            $"/api/teams/{team.Id}/invites/{revokedInviteId}/revoke",
            null,
            team.Version);
        Assert.Contains(team.Members, item => item.Id == revokedInviteId && item.Status == TeamMemberStatuses.Revoked);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", secondMember.AccessToken);
        using (var revokedAccept = VersionedRequest(
            HttpMethod.Post,
            $"/api/teams/{team.Id}/invites/accept",
            new TeamInviteTokenRequest(revokedToken),
            team.Version))
        {
            var response = await client.SendAsync(revokedAccept);
            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        }

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", owner.AccessToken);
        team = await SendVersionedAsync(
            HttpMethod.Post,
            $"/api/teams/{team.Id}/members",
            new InviteTeamMemberRequest(secondMember.User.Email, TeamRoles.Member),
            team.Version);
        var declinedToken = Assert.IsType<string>(team.InvitationToken);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", secondMember.AccessToken);
        team = await SendVersionedAsync(
            HttpMethod.Post,
            $"/api/teams/{team.Id}/invites/decline",
            new TeamInviteTokenRequest(declinedToken),
            team.Version);
        Assert.Contains(team.Members, item =>
            item.Email == secondMember.User.Email && item.Status == TeamMemberStatuses.Declined);

        var firstPage = await GetAsync<TeamMemberPageResponse>($"/api/teams/{team.Id}/members?pageSize=2");
        Assert.Equal(2, firstPage.Items.Count);
        Assert.NotNull(firstPage.NextCursor);
        Assert.All(firstPage.Items, item => Assert.False(string.IsNullOrWhiteSpace(item.DisplayName)));
        var secondPage = await GetAsync<TeamMemberPageResponse>(
            $"/api/teams/{team.Id}/members?pageSize=2&afterMemberId={firstPage.NextCursor}");
        Assert.NotEmpty(secondPage.Items);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", owner.AccessToken);
        team = await SendVersionedAsync(
            HttpMethod.Post,
            $"/api/teams/{team.Id}/ownership-transfer",
            new TransferTeamOwnershipRequest(member.User.Id),
            team.Version);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", member.AccessToken);
        using (var removeOwner = VersionedRequest(
            HttpMethod.Delete,
            $"/api/teams/{team.Id}/members/{member.User.Id}",
            null,
            team.Version))
        {
            var response = await client.SendAsync(removeOwner);
            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
            var body = await response.Content.ReadFromJsonAsync<ApiResponse<object>>();
            Assert.Equal("TEAM_LAST_OWNER", body!.Error!.Code);
        }

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", owner.AccessToken);
        organization = await SendOrganizationVersionedAsync(
            $"/api/organizations/{organization.Id}/suspend",
            new SuspendOrganizationRequest("team lifecycle test"),
            organization.Version);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", member.AccessToken);
        using (var inactiveUpdate = VersionedRequest(
            HttpMethod.Put,
            $"/api/teams/{team.Id}",
            new UpdateTeamRequest("Blocked"),
            team.Version))
        {
            var response = await client.SendAsync(inactiveUpdate);
            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
            var body = await response.Content.ReadFromJsonAsync<ApiResponse<object>>();
            Assert.Equal("TEAM_ORGANIZATION_INACTIVE", body!.Error!.Code);
        }

        var audit = await GetAsync<AuditLogPageResponse>(
            $"/api/audit?entityType=Team&entityId={team.Id}&page=1&pageSize=50");
        Assert.Contains(audit.Items, entry => entry.Action == "TeamMemberInvited");
        Assert.Contains(audit.Items, entry => entry.Action == "TeamInviteAccepted");
        Assert.Contains(audit.Items, entry => entry.Action == "TeamInviteRevoked");
        Assert.Contains(audit.Items, entry => entry.Action == "TeamInviteDeclined");
        Assert.Contains(audit.Items, entry => entry.Action == "TeamOwnershipTransferred");
    }

    private async Task<AuthResponse> RegisterAsync(string username, string organizationId) =>
        await PostAsync<AuthResponse>("/api/auth/register", new RegisterUserRequest(
            username,
            $"{username}@zumbo.local",
            "P@ssword123",
            organizationId));

    private async Task<NotificationResponse> WaitForInvitationNotificationAsync()
    {
        for (var attempt = 0; attempt < 30; attempt++)
        {
            var notifications = await GetAsync<IReadOnlyList<NotificationResponse>>("/api/notifications");
            var match = notifications.FirstOrDefault(item => item.Type == "TeamInvitation");
            if (match is not null)
            {
                return match;
            }

            await Task.Delay(100);
        }

        throw new Xunit.Sdk.XunitException("Durable team invitation notification was not delivered.");
    }

    private async Task<string> GetRawTeamListAsync(string organizationId)
    {
        var response = await client.GetAsync($"/api/teams?organizationId={organizationId}");
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync();
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

    private async Task<TeamResponse> SendVersionedAsync(
        HttpMethod method,
        string url,
        object? body,
        long expectedVersion)
    {
        using var request = VersionedRequest(method, url, body, expectedVersion);
        var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        var envelope = await response.Content.ReadFromJsonAsync<ApiResponse<TeamResponse>>();
        Assert.Equal($"\"{envelope!.Data!.Version}\"", response.Headers.ETag?.Tag);
        return envelope.Data;
    }

    private async Task<OrganizationResponse> SendOrganizationVersionedAsync(
        string url,
        object body,
        long expectedVersion)
    {
        using var request = VersionedRequest(HttpMethod.Post, url, body, expectedVersion);
        var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ApiResponse<OrganizationResponse>>())!.Data!;
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
