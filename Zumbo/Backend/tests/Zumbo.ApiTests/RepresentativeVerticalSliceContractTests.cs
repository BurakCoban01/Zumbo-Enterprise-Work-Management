using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Zumbo.Modules.Audit;
using Zumbo.Modules.Boards;
using Zumbo.Modules.Identity;
using Zumbo.Modules.Notifications;
using Zumbo.Modules.Notifications.Application.Policies;
using Zumbo.Modules.Organizations;
using Zumbo.Modules.Projects;
using Zumbo.Modules.Teams;
using Zumbo.Modules.Workflows;
using Zumbo.Modules.WorkItems;
using Zumbo.SharedKernel;

namespace Zumbo.ApiTests;

public sealed class RepresentativeVerticalSliceContractTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;

    public RepresentativeVerticalSliceContractTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder => builder.ConfigureAppConfiguration((_, configuration) =>
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["IdentityBootstrap:BootstrapToken"] = "development-bootstrap-token"
            })));
        _client = _factory.CreateClient();
    }

    [Fact]
    public async Task RepresentativeSlices_PreserveRoutesStatusesBodiesHeadersAndNegativePaths()
    {
        var stamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString();
        var tenantId = "vs-tenant-" + stamp;
        var registrationResponse = await _client.PostAsJsonAsync(
            "/api/auth/register",
            new RegisterUserRequest(
                "vsuser" + stamp,
                $"vs-user-{stamp}@zumbo.local",
                "P@ssword123",
                tenantId));
        var registration = await AssertSuccessAsync<AuthResponse>(registrationResponse, HttpStatusCode.OK);

        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", registration.AccessToken);

        var users = await AssertSuccessAsync<IReadOnlyList<UserProfileResponse>>(
            await _client.GetAsync("/api/auth/users?search=vsuser"),
            HttpStatusCode.OK);
        Assert.Contains(users, user => user.Id == registration.User.Id);

        var organizationResponse = await _client.PostAsJsonAsync(
            "/api/organizations",
            new CreateOrganizationRequest("Vertical Slice Organization", tenantId));
        Assert.Null(organizationResponse.Headers.Location);
        var organization = await AssertSuccessAsync<OrganizationResponse>(organizationResponse, HttpStatusCode.Created);
        var organizations = await AssertSuccessAsync<IReadOnlyList<OrganizationResponse>>(
            await _client.GetAsync("/api/organizations"),
            HttpStatusCode.OK);
        Assert.Contains(organizations, item => item.Id == organization.Id);

        var teamResponse = await _client.PostAsJsonAsync(
            "/api/teams",
            new CreateTeamRequest(tenantId, "Vertical Slice Team", registration.User.Id));
        Assert.Null(teamResponse.Headers.Location);
        var team = await AssertSuccessAsync<TeamResponse>(teamResponse, HttpStatusCode.Created);
        var teams = await AssertSuccessAsync<IReadOnlyList<TeamResponse>>(
            await _client.GetAsync($"/api/teams?organizationId={tenantId}"),
            HttpStatusCode.OK);
        Assert.Contains(teams, item => item.Id == team.Id);

        var projectResponse = await _client.PostAsJsonAsync(
            "/api/projects",
            new CreateProjectRequest(tenantId, "VS" + stamp[^6..], "Vertical Slice Project", registration.User.Id));
        Assert.Null(projectResponse.Headers.Location);
        var project = await AssertSuccessAsync<ProjectResponse>(projectResponse, HttpStatusCode.Created);
        var projects = await AssertSuccessAsync<IReadOnlyList<ProjectResponse>>(
            await _client.GetAsync($"/api/projects?organizationId={tenantId}"),
            HttpStatusCode.OK);
        Assert.Contains(projects, item => item.Id == project.Id);

        var boardResponse = await _client.PostAsJsonAsync(
            "/api/boards",
            new CreateBoardRequest(project.Id, "Vertical Slice Board", "Kanban"));
        Assert.Null(boardResponse.Headers.Location);
        var board = await AssertSuccessAsync<BoardResponse>(boardResponse, HttpStatusCode.Created);
        var boards = await AssertSuccessAsync<IReadOnlyList<BoardResponse>>(
            await _client.GetAsync($"/api/boards/by-project/{project.Id}"),
            HttpStatusCode.OK);
        Assert.Contains(boards, item => item.Id == board.Id);

        var defaultWorkflow = await AssertSuccessAsync<WorkflowResponse>(
            await _client.GetAsync($"/api/workflows/{project.Id}"),
            HttpStatusCode.OK);
        var workflowRequest = new CreateWorkflowRequest(
            "body-project-id-must-be-overridden",
            defaultWorkflow.Transitions.Select(transition => new WorkflowTransitionRequest(
                transition.FromStatus,
                transition.ToStatus,
                transition.RequiresAssignee,
                transition.RequiresCompletedChecklist,
                transition.RequiresApproval,
                transition.Automations.Select(automation =>
                    new WorkflowAutomationRequest(automation.Action, automation.Value)).ToList())).ToList(),
            defaultWorkflow.Statuses.Select(status => new WorkflowStatusRequest(status.Name, status.Category)).ToList());
        var workflow = await AssertSuccessAsync<WorkflowResponse>(
            await _client.PutAsJsonAsync($"/api/workflows/{project.Id}", workflowRequest),
            HttpStatusCode.OK);
        Assert.Equal(project.Id, workflow.ProjectId);

        var workItemResponse = await _client.PostAsJsonAsync(
            "/api/work-items",
            new CreateWorkItemRequest(
                project.Id,
                board.Id,
                "Vertical slice contract",
                "Task",
                "Medium",
                registration.User.Id,
                null));
        Assert.Null(workItemResponse.Headers.Location);
        var workItem = await AssertSuccessAsync<WorkItemResponse>(workItemResponse, HttpStatusCode.Created);
        var workItems = await AssertSuccessAsync<IReadOnlyList<WorkItemResponse>>(
            await _client.GetAsync($"/api/work-items?projectId={project.Id}"),
            HttpStatusCode.OK);
        Assert.Contains(workItems, item => item.Id == workItem.Id);

        var notification = await EventuallyAsync(async () =>
        {
            var notifications = await AssertSuccessAsync<IReadOnlyList<NotificationResponse>>(
                await _client.GetAsync("/api/notifications?page=1&pageSize=10"),
                HttpStatusCode.OK);
            return notifications.SingleOrDefault(item => item.Type == "Assignment");
        });
        Assert.Equal(NotificationCategories.Action, notification.Category);
        Assert.Equal(NotificationActionKinds.OpenWorkItem, notification.ActionKind);
        Assert.Equal("WorkItem", notification.SourceKind);
        Assert.Equal(workItem.Id, notification.SourceId);
        Assert.Equal(project.Id, notification.ProjectId);
        var readResult = await AssertSuccessAsync<MarkNotificationAsReadResponse>(
            await _client.PatchAsJsonAsync($"/api/notifications/{notification.Id}/read", new { }),
            HttpStatusCode.OK);
        Assert.True(readResult.Read);

        var audit = await AssertSuccessAsync<AuditLogPageResponse>(
            await _client.GetAsync($"/api/audit?entityType=Board&entityId={board.Id}&page=1&pageSize=10"),
            HttpStatusCode.OK);
        Assert.Contains(audit.Items, item => item.Action == "BoardCreated" && item.EntityId == board.Id);

        await AssertFailureAsync(
            await _client.PostAsJsonAsync("/api/organizations", new CreateOrganizationRequest("", tenantId)),
            HttpStatusCode.BadRequest,
            "VALIDATION_ERROR");
        await AssertFailureAsync(
            await _client.GetAsync("/api/work-items?page=1&pageSize=10"),
            HttpStatusCode.BadRequest,
            "VALIDATION_ERROR");
        await AssertFailureAsync(
            await _client.GetAsync("/api/notifications?page=0&pageSize=10"),
            HttpStatusCode.BadRequest,
            "REQUEST_LIMIT_EXCEEDED");
        await AssertFailureAsync(
            await _client.GetAsync("/api/audit?entityType=Board"),
            HttpStatusCode.BadRequest,
            "VALIDATION_ERROR");

        using var anonymousClient = _factory.CreateClient();
        await AssertAuthenticationChallengeAsync(
            await anonymousClient.GetAsync("/api/organizations"),
            HttpStatusCode.Unauthorized);
    }

    private static async Task<T> AssertSuccessAsync<T>(HttpResponseMessage response, HttpStatusCode expectedStatus)
    {
        Assert.Equal(expectedStatus, response.StatusCode);
        var correlationId = Assert.Single(response.Headers.GetValues("X-Correlation-Id"));
        var body = await response.Content.ReadFromJsonAsync<ApiResponse<T>>();

        Assert.NotNull(body);
        Assert.True(body.Success);
        Assert.Null(body.Error);
        Assert.Equal(correlationId, body.CorrelationId);
        Assert.NotNull(body.Data);
        return body.Data!;
    }

    private static async Task AssertFailureAsync(
        HttpResponseMessage response,
        HttpStatusCode expectedStatus,
        string expectedCode)
    {
        Assert.Equal(expectedStatus, response.StatusCode);
        var correlationId = Assert.Single(response.Headers.GetValues("X-Correlation-Id"));
        var body = await response.Content.ReadFromJsonAsync<ApiResponse<object>>();

        Assert.NotNull(body);
        Assert.False(body.Success);
        Assert.Null(body.Data);
        Assert.NotNull(body.Error);
        Assert.Equal(expectedCode, body.Error.Code);
        Assert.Equal(correlationId, body.CorrelationId);
    }

    private static async Task AssertAuthenticationChallengeAsync(
        HttpResponseMessage response,
        HttpStatusCode expectedStatus)
    {
        Assert.Equal(expectedStatus, response.StatusCode);
        Assert.Single(response.Headers.GetValues("X-Correlation-Id"));
        Assert.Empty(await response.Content.ReadAsStringAsync());
    }

    private static async Task<T> EventuallyAsync<T>(Func<Task<T?>> operation) where T : class
    {
        for (var attempt = 0; attempt < 200; attempt++)
        {
            var result = await operation();
            if (result is not null) return result;
            await Task.Delay(25);
        }

        throw new Xunit.Sdk.XunitException("The durable consumer result was not visible within the bounded wait.");
    }
}
