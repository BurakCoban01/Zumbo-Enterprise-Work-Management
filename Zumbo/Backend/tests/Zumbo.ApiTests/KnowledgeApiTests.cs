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

public sealed class KnowledgeApiTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> factory;

    public KnowledgeApiTests(WebApplicationFactory<Program> factory)
    {
        this.factory = factory;
    }

    [Fact]
    public async Task VersionHistorySearchCommentsAuthorizationAndArchiveAreAuthoritative()
    {
        using var client = factory.CreateClient();
        var stamp = Guid.NewGuid().ToString("N");
        var organizationId = "knowledge-" + stamp;
        var owner = await RegisterAsync(
            client,
            "knowledge-owner-" + stamp,
            organizationId);
        var viewer = await RegisterAsync(
            client,
            "knowledge-viewer-" + stamp,
            organizationId);
        var outsider = await RegisterAsync(
            client,
            "knowledge-outsider-" + stamp,
            "foreign-" + stamp);
        Authorize(client, owner);
        await PostAsync<OrganizationResponse>(
            client,
            "/api/organizations",
            new CreateOrganizationRequest(
                "Knowledge organization",
                organizationId));
        var project = await PostAsync<ProjectResponse>(
            client,
            "/api/projects",
            new CreateProjectRequest(
                organizationId,
                "KD" + stamp[..6],
                "Knowledge project",
                owner.User.Id));
        await PostAsync<ProjectResponse>(
            client,
            $"/api/projects/{project.Id}/members",
            new AddProjectMemberRequest(viewer.User.Id, ProjectRoles.Viewer));

        var createResponse = await client.PostAsJsonAsync(
            "/api/knowledge-documents",
            new CreateKnowledgeDocumentRequest(
                KnowledgeScopeTypes.Project,
                project.Id,
                "Authentication context",
                "# Context\nSynthetic authentication decision.",
                ["security"],
                [],
                [viewer.User.Id],
                "Initial authentication context."));
        createResponse.EnsureSuccessStatusCode();
        Assert.Equal("\"1\"", createResponse.Headers.ETag?.Tag);
        var document = (await createResponse.Content.ReadFromJsonAsync<
            ApiResponse<KnowledgeDocumentResponse>>())!.Data!;
        Assert.Equal(1, document.CurrentContentVersion);

        var updateResponse = await client.PutAsJsonAsync(
            $"/api/knowledge-documents/{document.Id}",
            new CreateKnowledgeVersionRequest(
                "Authentication decision",
                "# Decision\nUse [the recovery runbook](/runbooks/recovery).",
                ["security", "decision"],
                [],
                [viewer.User.Id],
                "Recorded the selected recovery boundary."));
        updateResponse.EnsureSuccessStatusCode();
        document = (await updateResponse.Content.ReadFromJsonAsync<
            ApiResponse<KnowledgeDocumentResponse>>())!.Data!;
        Assert.Equal(2, document.CurrentContentVersion);
        Assert.Equal([2, 1], document.Versions.Select(item => item.Number));
        var firstVersion = await GetAsync<KnowledgeVersionResponse>(
            client,
            $"/api/knowledge-documents/{document.Id}/versions/1");
        Assert.Equal("# Context\nSynthetic authentication decision.", firstVersion.ContentMarkdown);

        using (var stale = new HttpRequestMessage(
            HttpMethod.Put,
            $"/api/knowledge-documents/{document.Id}")
        {
            Content = JsonContent.Create(new CreateKnowledgeVersionRequest(
                "Stale decision",
                "Stale content",
                [],
                [],
                [],
                "Stale version."))
        })
        {
            stale.Headers.IfMatch.Add(new EntityTagHeaderValue("\"1\""));
            await AssertErrorAsync(
                await client.SendAsync(stale),
                HttpStatusCode.Conflict,
                "CONCURRENCY_CONFLICT");
        }

        var unsafeResponse = await client.PostAsJsonAsync(
            "/api/knowledge-documents",
            new CreateKnowledgeDocumentRequest(
                KnowledgeScopeTypes.Project,
                project.Id,
                "Unsafe content",
                "[unsafe](javascript:alert(1))",
                [],
                [],
                [],
                "Unsafe content."));
        await AssertErrorAsync(
            unsafeResponse,
            HttpStatusCode.BadRequest,
            "VALIDATION_ERROR");

        Authorize(client, viewer);
        var visible = await GetAsync<KnowledgeDocumentResponse>(
            client,
            $"/api/knowledge-documents/{document.Id}");
        Assert.False(visible.CanEdit);
        Assert.True(visible.CanComment);
        var commented = await PostAsync<KnowledgeDocumentResponse>(
            client,
            $"/api/knowledge-documents/{document.Id}/comments",
            new AddKnowledgeCommentRequest("Please clarify the recovery owner."));
        var comment = Assert.Single(commented.Comments);
        var resolvedResponse = await client.PatchAsync(
            $"/api/knowledge-documents/{document.Id}/comments/{comment.Id}/resolve",
            null);
        resolvedResponse.EnsureSuccessStatusCode();
        var resolved = (await resolvedResponse.Content.ReadFromJsonAsync<
            ApiResponse<KnowledgeDocumentResponse>>())!.Data!;
        Assert.True(Assert.Single(resolved.Comments).Resolved);
        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await client.PutAsJsonAsync(
                $"/api/knowledge-documents/{document.Id}",
                new CreateKnowledgeVersionRequest(
                    "Forbidden",
                    "Forbidden",
                    [],
                    [],
                    [],
                    "Forbidden."))).StatusCode);

        var search = await GetAsync<KnowledgeSearchResponse>(
            client,
            "/api/knowledge-documents?query=authentication&page=1&pageSize=20");
        Assert.Equal(KnowledgeSourceStatuses.Ready, search.SourceStatus);
        Assert.Equal(document.Id, Assert.Single(search.Items).Id);

        Authorize(client, outsider);
        await AssertErrorAsync(
            await client.GetAsync($"/api/knowledge-documents/{document.Id}"),
            HttpStatusCode.NotFound,
            "KNOWLEDGE_DOCUMENT_NOT_FOUND");
        client.DefaultRequestHeaders.Authorization = null;
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await client.GetAsync("/api/knowledge-documents")).StatusCode);

        Authorize(client, owner);
        var archive = await client.DeleteAsync(
            $"/api/knowledge-documents/{document.Id}");
        archive.EnsureSuccessStatusCode();
        var ownerList = await GetAsync<KnowledgeSearchResponse>(
            client,
            "/api/knowledge-documents?page=1&pageSize=100");
        Assert.Empty(ownerList.Items);
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

    private static void Authorize(HttpClient client, AuthResponse auth) =>
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", auth.AccessToken);

    private static async Task<T> PostAsync<T>(
        HttpClient client,
        string url,
        object body)
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
        var error = await response.Content.ReadFromJsonAsync<
            ApiResponse<JsonElement>>();
        Assert.Equal(code, error!.Error!.Code);
    }
}
