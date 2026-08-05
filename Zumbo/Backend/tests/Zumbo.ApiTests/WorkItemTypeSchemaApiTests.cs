using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Zumbo.Modules.Boards;
using Zumbo.Modules.Identity;
using Zumbo.Modules.Organizations;
using Zumbo.Modules.Projects;
using Zumbo.Modules.WorkItems;
using Zumbo.SharedKernel;

namespace Zumbo.ApiTests;

public sealed class WorkItemTypeSchemaApiTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient client;

    public WorkItemTypeSchemaApiTests(WebApplicationFactory<Program> factory) =>
        client = factory.CreateClient();

    [Fact]
    public async Task TypedFields_SearchReportsCompatibilityAndConcurrency_AreEnforced()
    {
        var stamp = Guid.NewGuid().ToString("N");
        var organizationId = "domain007-" + stamp;
        var owner = await PostAsync<AuthResponse>("/api/auth/register", new RegisterUserRequest(
            "domain007-owner-" + stamp,
            $"domain007-owner-{stamp}@zumbo.local",
            "P@ssword123",
            organizationId));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", owner.AccessToken);
        await PostAsync<OrganizationResponse>(
            "/api/organizations",
            new CreateOrganizationRequest("Domain 007", organizationId));
        var project = await PostAsync<ProjectResponse>("/api/projects", new CreateProjectRequest(
            organizationId,
            "IT" + stamp[..6],
            "Typed issues",
            owner.User.Id));
        var board = await PostAsync<BoardResponse>("/api/boards", new CreateBoardRequest(
            project.Id,
            "Incidents",
            "Kanban"));

        var initial = await GetAsync<WorkItemTypeSchemaResponse>($"/api/work-item-schemas/{project.Id}");
        Assert.Equal(5, initial.IssueTypes.Count);
        var request = SchemaRequest("Critical", "High", "Medium", "Low");
        var schema = await SendVersionedAsync<WorkItemTypeSchemaResponse>(
            HttpMethod.Put,
            $"/api/work-item-schemas/{project.Id}",
            request,
            initial.Version);
        Assert.Single(schema.IssueTypes);
        Assert.Equal("Incident", schema.IssueTypes.Single().Key);

        var invalid = await client.PostAsJsonAsync("/api/work-items", new CreateWorkItemRequest(
            project.Id,
            board.Id,
            "Invalid incident",
            "Incident",
            "High",
            owner.User.Id,
            null,
            CustomFields:
            [
                new WorkItemCustomFieldValueRequest("severity", TextValue: "Critical")
            ]));
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);

        var incident = await PostAsync<WorkItemResponse>("/api/work-items", new CreateWorkItemRequest(
            project.Id,
            board.Id,
            "Database unavailable",
            "incident",
            "High",
            owner.User.Id,
            null,
            CustomFields:
            [
                new WorkItemCustomFieldValueRequest("severity", OptionKey: "Critical"),
                new WorkItemCustomFieldValueRequest("customer", TextValue: "Acme")
            ]));
        Assert.Equal("Incident", incident.Type);
        Assert.Equal(schema.SchemaVersion, incident.IssueTypeSchemaVersion);
        Assert.Equal(2, incident.CustomFields!.Count);

        incident = await SendVersionedAsync<WorkItemResponse>(
            HttpMethod.Put,
            $"/api/work-items/{incident.Id}/custom-fields",
            new SetWorkItemCustomFieldsRequest(
            [
                new WorkItemCustomFieldValueRequest("severity", OptionKey: "Critical"),
                new WorkItemCustomFieldValueRequest("customer", TextValue: "Acme Enterprise")
            ]),
            incident.Version);
        Assert.Contains(incident.CustomFields!, field =>
            field.FieldKey == "customer" && field.TextValue == "Acme Enterprise");

        var searchUrl = $"/api/work-items?projectId={project.Id}&issueType=incident"
            + "&customFieldKey=SEVERITY&customFieldValue=critical";
        var search = await EventuallyAsync(searchUrl, incident.Id);
        Assert.Single(search);

        var issueTypeReport = await GetAsync<WorkItemFieldDistributionResponse>(
            $"/api/work-item-schemas/{project.Id}/reports/issue-types");
        Assert.Equal(1, issueTypeReport.TotalItems);
        Assert.Contains(issueTypeReport.Values, entry => entry.Value == "Incident" && entry.Count == 1);
        var severityReport = await GetAsync<WorkItemFieldDistributionResponse>(
            $"/api/work-item-schemas/{project.Id}/reports/custom-fields/severity");
        Assert.Equal(0, severityReport.MissingItems);
        Assert.Contains(severityReport.Values, entry => entry.Value == "Critical" && entry.Count == 1);

        using var incompatibleRequest = VersionedRequest(
            HttpMethod.Put,
            $"/api/work-item-schemas/{project.Id}",
            SchemaRequest("High", "Medium", "Low"),
            schema.Version);
        var incompatible = await client.SendAsync(incompatibleRequest);
        await AssertErrorAsync(
            incompatible,
            HttpStatusCode.Conflict,
            "WORK_ITEM_SCHEMA_EXISTING_VALUE_INVALID");

        using var firstUpdate = VersionedRequest(
            HttpMethod.Put,
            $"/api/work-item-schemas/{project.Id}",
            request,
            schema.Version);
        using var secondUpdate = VersionedRequest(
            HttpMethod.Put,
            $"/api/work-item-schemas/{project.Id}",
            request,
            schema.Version);
        var concurrent = await Task.WhenAll(client.SendAsync(firstUpdate), client.SendAsync(secondUpdate));
        Assert.Single(concurrent, response => response.StatusCode == HttpStatusCode.OK);
        Assert.Single(concurrent, response => response.StatusCode == HttpStatusCode.Conflict);
    }

    private static UpsertWorkItemTypeSchemaRequest SchemaRequest(params string[] severities) => new(
        [new IssueTypeDefinitionRequest("Incident", "Incident", "Operational incident", "Standard")],
        [
            new CustomFieldDefinitionRequest(
                "severity", "Severity", "Select", true, true, null, null, null, severities, ["Incident"]),
            new CustomFieldDefinitionRequest(
                "customer", "Customer", "Text", false, true, 100, null, null, null, ["Incident"])
        ],
        [new IssueTypeLayoutRequest("Incident", ["severity", "customer"])]);

    private async Task<IReadOnlyCollection<WorkItemResponse>> EventuallyAsync(string url, string expectedId)
    {
        IReadOnlyCollection<WorkItemResponse> latest = [];
        for (var attempt = 0; attempt < 200; attempt++)
        {
            latest = await GetAsync<IReadOnlyCollection<WorkItemResponse>>(url);
            if (latest.Any(item => item.Id == expectedId))
            {
                return latest;
            }

            await Task.Delay(25);
        }

        throw new Xunit.Sdk.XunitException("Indexed custom field was not searchable in time.");
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

    private async Task<T> SendVersionedAsync<T>(
        HttpMethod method,
        string url,
        object body,
        long expectedVersion)
    {
        using var request = VersionedRequest(method, url, body, expectedVersion);
        var response = await client.SendAsync(request);
        if (!response.IsSuccessStatusCode)
        {
            throw new Xunit.Sdk.XunitException(await response.Content.ReadAsStringAsync());
        }
        return (await response.Content.ReadFromJsonAsync<ApiResponse<T>>())!.Data!;
    }

    private static HttpRequestMessage VersionedRequest(
        HttpMethod method,
        string url,
        object body,
        long expectedVersion)
    {
        var request = new HttpRequestMessage(method, url) { Content = JsonContent.Create(body) };
        if (expectedVersion > 0)
        {
            request.Headers.TryAddWithoutValidation("If-Match", $"\"{expectedVersion}\"");
        }
        return request;
    }

    private static async Task AssertErrorAsync(
        HttpResponseMessage response,
        HttpStatusCode status,
        string code)
    {
        Assert.Equal(status, response.StatusCode);
        var envelope = await response.Content.ReadFromJsonAsync<ApiResponse<object>>();
        Assert.Equal(code, envelope!.Error!.Code);
    }
}
