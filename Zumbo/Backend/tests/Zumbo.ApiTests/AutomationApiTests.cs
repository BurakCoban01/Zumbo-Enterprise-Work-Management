using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Zumbo.Modules.Identity;
using Zumbo.Modules.Organizations;
using Zumbo.Modules.Projects;
using Zumbo.Modules.Boards;
using Zumbo.Modules.Workflows;
using Zumbo.Modules.WorkItems;
using Zumbo.SharedKernel;

namespace Zumbo.ApiTests;

public sealed class AutomationApiTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> factory;

    public AutomationApiTests(WebApplicationFactory<Program> factory)
    {
        this.factory = factory;
    }

    [Fact]
    public async Task DraftPublishDryRunConflictAndTenantIsolation_AreAuthoritative()
    {
        using var client = factory.CreateClient();
        var stamp = Guid.NewGuid().ToString("N");
        var organizationId = "automation-" + stamp;
        var owner = await RegisterAsync(client, "automation-owner-" + stamp, organizationId);
        var outsider = await RegisterAsync(client, "automation-outsider-" + stamp, "foreign-" + stamp);
        Authorize(client, owner);
        await PostAsync<OrganizationResponse>(
            client,
            "/api/organizations",
            new CreateOrganizationRequest("Automation organization", organizationId));
        var project = await PostAsync<ProjectResponse>(
            client,
            "/api/projects",
            new CreateProjectRequest(
                organizationId,
                "AU" + stamp[..6],
                "Automation project",
                owner.User.Id));

        var createdResponse = await client.PostAsJsonAsync(
            "/api/automations",
            Rule(project.Id, "Urgent escalation"));
        Assert.Equal(HttpStatusCode.Created, createdResponse.StatusCode);
        Assert.Equal("\"1\"", createdResponse.Headers.ETag!.Tag);
        var draft = (await createdResponse.Content.ReadFromJsonAsync<ApiResponse<AutomationRuleResponse>>())!.Data!;
        Assert.True(draft.HasDraft);
        Assert.Equal(0, draft.PublishedVersion);

        using var publishRequest = new HttpRequestMessage(
            HttpMethod.Post,
            $"/api/automations/{draft.Id}/publish");
        publishRequest.Headers.IfMatch.Add(new EntityTagHeaderValue("\"1\""));
        var publishResponse = await client.SendAsync(publishRequest);
        publishResponse.EnsureSuccessStatusCode();
        var published = (await publishResponse.Content.ReadFromJsonAsync<ApiResponse<AutomationRuleResponse>>())!.Data!;
        Assert.True(published.Active);
        Assert.Equal(1, published.PublishedVersion);
        Assert.False(published.HasDraft);
        Assert.Equal("\"2\"", publishResponse.Headers.ETag!.Tag);

        var dryRun = await PostAsync<AutomationDryRunResponse>(
            client,
            $"/api/automations/{draft.Id}/dry-run",
            new AutomationDryRunContext(
                "Event",
                "WorkItemTransitioned",
                "work-1",
                new Dictionary<string, string?>
                {
                    ["Priority"] = "High",
                    ["Labels"] = "triage,customer"
                }));
        Assert.Equal("WouldExecute", dryRun.Outcome);
        Assert.Equal(["AddLabel", "SetPriority"], dryRun.PlannedActions.Select(action => action.Type));

        using var staleDraftRequest = new HttpRequestMessage(
            HttpMethod.Put,
            $"/api/automations/{draft.Id}/draft")
        {
            Content = JsonContent.Create(Rule(project.Id, "Stale edit"))
        };
        staleDraftRequest.Headers.IfMatch.Add(new EntityTagHeaderValue("\"1\""));
        await AssertErrorAsync(
            await client.SendAsync(staleDraftRequest),
            HttpStatusCode.Conflict,
            "CONCURRENCY_CONFLICT");

        Authorize(client, outsider);
        await AssertErrorAsync(
            await client.GetAsync($"/api/automations?projectId={project.Id}"),
            HttpStatusCode.NotFound,
            "PROJECT_NOT_FOUND");

        client.DefaultRequestHeaders.Authorization = null;
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await client.GetAsync($"/api/automations?projectId={project.Id}")).StatusCode);
    }

    [Fact]
    public async Task PublishedCreateRule_ExecutesThroughDurableEventAndExposesRunHistory()
    {
        using var client = factory.CreateClient();
        var stamp = Guid.NewGuid().ToString("N");
        var organizationId = "automation-runtime-" + stamp;
        var owner = await RegisterAsync(client, "automation-runtime-owner-" + stamp, organizationId);
        Authorize(client, owner);
        await PostAsync<OrganizationResponse>(
            client,
            "/api/organizations",
            new CreateOrganizationRequest("Automation runtime organization", organizationId));
        var project = await PostAsync<ProjectResponse>(
            client,
            "/api/projects",
            new CreateProjectRequest(
                organizationId,
                "AR" + stamp[..6],
                "Automation runtime project",
                owner.User.Id));
        var board = await PostAsync<BoardResponse>(
            client,
            "/api/boards",
            new CreateBoardRequest(project.Id, "Automation board", "Kanban"));

        var draft = await PostAsync<AutomationRuleResponse>(
            client,
            "/api/automations",
            new DefineAutomationRuleRequest(
                project.Id,
                "Mark new work",
                null,
                new AutomationTriggerRequest("Event", "WorkItemCreated"),
                new AutomationConditionRequest("Field", "Priority", "Equals", "High"),
                [new AutomationActionRequest("AddLabel", "new-high-priority")]));
        using (var publish = new HttpRequestMessage(
            HttpMethod.Post,
            $"/api/automations/{draft.Id}/publish"))
        {
            publish.Headers.IfMatch.Add(new EntityTagHeaderValue($"\"{draft.Version}\""));
            (await client.SendAsync(publish)).EnsureSuccessStatusCode();
        }

        var item = await PostAsync<WorkItemResponse>(
            client,
            "/api/work-items",
            new CreateWorkItemRequest(
                project.Id,
                board.Id,
                "High priority customer request",
                "Task",
                "High",
                owner.User.Id,
                null));

        var completedRun = await EventuallyAsync(
            async () =>
            {
                var response = await client.GetAsync(
                    $"/api/automations/runs?projectId={project.Id}&ruleId={draft.Id}");
                response.EnsureSuccessStatusCode();
                return (await response.Content.ReadFromJsonAsync<
                    ApiResponse<AutomationRunPageResponse>>())!.Data!;
            },
            page => page.Items.Count == 1
                && page.Items.Single().Status == AutomationRunStates.Succeeded,
            "Automation run did not complete through the durable event worker.");
        var run = Assert.Single(completedRun.Items);
        Assert.Equal(item.Id, run.SourceId);
        Assert.Equal(AutomationStepStates.Succeeded, Assert.Single(run.Steps).Status);

        var updatedItem = await EventuallyAsync(
            async () =>
            {
                var response = await client.GetAsync($"/api/work-items/{item.Id}");
                response.EnsureSuccessStatusCode();
                return (await response.Content.ReadFromJsonAsync<
                    ApiResponse<WorkItemResponse>>())!.Data!;
            },
            value => value.Labels.Contains("new-high-priority"),
            "Automation action did not update the work item.");
        Assert.Contains("new-high-priority", updatedItem.Labels);

        var detailResponse = await client.GetAsync($"/api/automations/runs/{run.Id}");
        detailResponse.EnsureSuccessStatusCode();
        Assert.Equal($"\"{run.Version}\"", detailResponse.Headers.ETag?.Tag);
    }

    [Fact]
    public async Task Developer_CanViewAutomationButCannotManageRules()
    {
        using var client = factory.CreateClient();
        var stamp = Guid.NewGuid().ToString("N");
        var organizationId = "automation-permission-" + stamp;
        var owner = await RegisterAsync(client, "automation-permission-owner-" + stamp, organizationId);
        var developer = await RegisterAsync(
            client,
            "automation-permission-developer-" + stamp,
            organizationId);
        Authorize(client, owner);
        await PostAsync<OrganizationResponse>(
            client,
            "/api/organizations",
            new CreateOrganizationRequest("Automation permission organization", organizationId));
        var project = await PostAsync<ProjectResponse>(
            client,
            "/api/projects",
            new CreateProjectRequest(
                organizationId,
                "AP" + stamp[..6],
                "Automation permission project",
                owner.User.Id));
        await PostAsync<ProjectResponse>(
            client,
            $"/api/projects/{project.Id}/members",
            new AddProjectMemberRequest(developer.User.Id, ProjectRoles.Developer));

        Authorize(client, developer);
        Assert.Equal(
            HttpStatusCode.OK,
            (await client.GetAsync($"/api/automations?projectId={project.Id}")).StatusCode);
        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await client.PostAsJsonAsync(
                "/api/automations",
                Rule(project.Id, "Developer cannot manage"))).StatusCode);
    }

    private static DefineAutomationRuleRequest Rule(string projectId, string name) =>
        new(
            projectId,
            name,
            "Escalates urgent triage work.",
            new AutomationTriggerRequest("Event", "WorkItemTransitioned"),
            new AutomationConditionRequest("All", Children:
            [
                new("Field", "Priority", "Equals", "High"),
                new("Field", "Labels", "Contains", "triage")
            ]),
            [
                new("AddLabel", "automated"),
                new("SetPriority", "Critical")
            ],
            MaximumExecutionsPerHour: 20,
            MaximumChainDepth: 3);

    private static async Task<AuthResponse> RegisterAsync(
        HttpClient client,
        string username,
        string organizationId) =>
        await PostAsync<AuthResponse>(
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

    private static async Task<T> PostAsync<T>(HttpClient client, string url, object body)
    {
        var response = await client.PostAsJsonAsync(url, body);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ApiResponse<T>>())!.Data!;
    }

    private static async Task AssertErrorAsync(
        HttpResponseMessage response,
        HttpStatusCode status,
        string code)
    {
        Assert.Equal(status, response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<ApiResponse<JsonElement>>();
        Assert.Equal(code, error!.Error!.Code);
    }

    private static async Task<T> EventuallyAsync<T>(
        Func<Task<T>> read,
        Func<T, bool> predicate,
        string failureMessage)
    {
        for (var attempt = 0; attempt < 80; attempt++)
        {
            var value = await read();
            if (predicate(value))
                return value;
            await Task.Delay(50);
        }

        throw new Xunit.Sdk.XunitException(failureMessage);
    }
}
