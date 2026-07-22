using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Zumbo.Modules.Audit;
using Zumbo.Modules.Boards;
using Zumbo.Modules.Identity;
using Zumbo.Modules.Organizations;
using Zumbo.Modules.Projects;
using Zumbo.Modules.WorkItems;
using Zumbo.Modules.Workflows;
using Zumbo.SharedKernel;

namespace Zumbo.ApiTests;

public sealed class WorkflowBoardLifecycleApiTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient client;

    public WorkflowBoardLifecycleApiTests(WebApplicationFactory<Program> factory)
    {
        client = factory.CreateClient();
    }

    [Fact]
    public async Task DraftPublishIssueSchemeBoardMappingAndAtomicWip_AreConsistent()
    {
        var stamp = Guid.NewGuid().ToString("N");
        var organizationId = "domain004-" + stamp;
        var owner = await PostAsync<AuthResponse>("/api/auth/register", new RegisterUserRequest(
            "domain004-owner-" + stamp,
            $"domain004-owner-{stamp}@zumbo.local",
            "P@ssword123",
            organizationId));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", owner.AccessToken);
        await PostAsync<OrganizationResponse>(
            "/api/organizations",
            new CreateOrganizationRequest("Domain 004", organizationId));
        var project = await PostAsync<ProjectResponse>("/api/projects", new CreateProjectRequest(
            organizationId,
            "WF" + stamp[..6],
            "Versioned Workflow",
            owner.User.Id));
        var board = await PostAsync<BoardResponse>("/api/boards", new CreateBoardRequest(
            project.Id,
            "Mapped Board",
            "Kanban"));
        var published = await GetAsync<WorkflowResponse>($"/api/workflows/{project.Id}");
        Assert.Equal(1, published.PublishedVersion);
        Assert.False(published.HasDraft);
        Assert.Contains(published.IssueTypeSchemes!, x => x.IssueType == "*");
        Assert.Contains(
            board.Columns.Single(x => x.Category == "InProgress").StatusNames!,
            x => x == "Blocked");

        var statuses = new[]
        {
            new WorkflowStatusRequest("To Do", "Todo"),
            new WorkflowStatusRequest("In Progress", "InProgress"),
            new WorkflowStatusRequest("QA", "InProgress"),
            new WorkflowStatusRequest("Done", "Done")
        };
        var transitions = new[]
        {
            new WorkflowTransitionRequest("To Do", "In Progress", false, false),
            new WorkflowTransitionRequest("In Progress", "QA", false, false),
            new WorkflowTransitionRequest("QA", "Done", false, false),
            new WorkflowTransitionRequest("To Do", "Done", false, false)
        };
        var schemes = new[]
        {
            new WorkflowIssueTypeSchemeRequest("Task", "To Do", ["To Do", "In Progress", "QA", "Done"], ["Done"]),
            new WorkflowIssueTypeSchemeRequest("Bug", "To Do", ["To Do", "Done"], ["Done"])
        };
        var draft = await SendVersionedAsync<WorkflowResponse>(
            HttpMethod.Put,
            $"/api/workflows/{project.Id}/draft",
            new CreateWorkflowRequest(project.Id, transitions, statuses, schemes),
            published.Version);
        Assert.True(draft.HasDraft);
        Assert.Equal(2, draft.PublishedVersion);
        published = await GetAsync<WorkflowResponse>($"/api/workflows/{project.Id}");
        Assert.Equal(1, published.PublishedVersion);
        Assert.DoesNotContain(published.Statuses, x => x.Name == "QA");

        var todo = board.Columns.Single(x => x.Category == "Todo");
        var inProgress = board.Columns.Single(x => x.Category == "InProgress");
        var review = board.Columns.Single(x => x.Category == "Review");
        var test = board.Columns.Single(x => x.Category == "Test");
        var done = board.Columns.Single(x => x.Category == "Done");
        board = await SendVersionedAsync<BoardResponse>(
            HttpMethod.Put,
            $"/api/boards/{board.Id}/workflow-mapping",
            new ConfigureBoardWorkflowMappingRequest(
            [
                new(todo.Id, ["To Do"]),
                new(inProgress.Id, ["In Progress"]),
                new(review.Id, []),
                new(test.Id, ["QA"]),
                new(done.Id, ["Done"])
            ]),
            board.Version);
        inProgress = board.Columns.Single(x => x.Id == inProgress.Id);
        board = await SendVersionedAsync<BoardResponse>(
            HttpMethod.Put,
            $"/api/boards/{board.Id}/columns/{inProgress.Id}",
            new UpdateColumnRequest(inProgress.Name, inProgress.Category, 1, inProgress.StatusNames),
            board.Version);

        published = await SendVersionedAsync<WorkflowResponse>(
            HttpMethod.Post,
            $"/api/workflows/{project.Id}/publish",
            null,
            draft.Version);
        Assert.Equal(2, published.PublishedVersion);
        Assert.False(published.HasDraft);
        var versions = await GetAsync<IReadOnlyCollection<WorkflowVersionResponse>>(
            $"/api/workflows/{project.Id}/versions");
        Assert.Equal([2, 1], versions.Select(x => x.Number));

        var first = await PostAsync<WorkItemResponse>("/api/work-items", new CreateWorkItemRequest(
            project.Id, board.Id, "First", "Task", "Medium", owner.User.Id, null));
        var second = await PostAsync<WorkItemResponse>("/api/work-items", new CreateWorkItemRequest(
            project.Id, board.Id, "Second", "Task", "Medium", owner.User.Id, null));
        var moves = await Task.WhenAll(
            client.PatchAsJsonAsync($"/api/work-items/{first.Id}/status", new MoveWorkItemRequest("In Progress")),
            client.PatchAsJsonAsync($"/api/work-items/{second.Id}/status", new MoveWorkItemRequest("In Progress")));
        Assert.Single(moves, x => x.StatusCode == HttpStatusCode.OK);
        Assert.Single(moves, x => x.StatusCode == HttpStatusCode.Conflict);
        var active = moves[0].StatusCode == HttpStatusCode.OK ? first : second;

        var bug = await PostAsync<WorkItemResponse>("/api/work-items", new CreateWorkItemRequest(
            project.Id, board.Id, "Restricted bug", "Bug", "High", owner.User.Id, null));
        var forbiddenBugMove = await client.PatchAsJsonAsync(
            $"/api/work-items/{bug.Id}/status",
            new MoveWorkItemRequest("In Progress"));
        await AssertErrorAsync(
            forbiddenBugMove,
            HttpStatusCode.Conflict,
            "WORKFLOW_ISSUE_SCHEME_TRANSITION_FORBIDDEN");

        var invalidMapping = new ConfigureBoardWorkflowMappingRequest(
        [
            new(todo.Id, ["To Do"]),
            new(inProgress.Id, ["QA"]),
            new(review.Id, []),
            new(test.Id, ["In Progress"]),
            new(done.Id, ["Done"])
        ]);
        using (var mappingRequest = VersionedRequest(
            HttpMethod.Put,
            $"/api/boards/{board.Id}/workflow-mapping",
            invalidMapping,
            board.Version))
        {
            await AssertErrorAsync(
                await client.SendAsync(mappingRequest),
                HttpStatusCode.Conflict,
                "BOARD_MAPPING_EXISTING_ITEM_INVALID");
        }

        var nextStatuses = new[]
        {
            new WorkflowStatusRequest("To Do", "Todo"),
            new WorkflowStatusRequest("QA", "InProgress"),
            new WorkflowStatusRequest("Done", "Done")
        };
        var nextTransitions = new[]
        {
            new WorkflowTransitionRequest("To Do", "QA", false, false),
            new WorkflowTransitionRequest("QA", "Done", false, false)
        };
        draft = await SendVersionedAsync<WorkflowResponse>(
            HttpMethod.Put,
            $"/api/workflows/{project.Id}/draft",
            new CreateWorkflowRequest(project.Id, nextTransitions, nextStatuses),
            published.Version);
        using var publishInvalid = VersionedRequest(
            HttpMethod.Post,
            $"/api/workflows/{project.Id}/publish",
            null,
            draft.Version);
        await AssertErrorAsync(
            await client.SendAsync(publishInvalid),
            HttpStatusCode.Conflict,
            "WORKFLOW_PUBLISH_EXISTING_STATUS_INVALID");
        published = await GetAsync<WorkflowResponse>($"/api/workflows/{project.Id}");
        Assert.Equal(2, published.PublishedVersion);
        Assert.True(published.HasDraft);
        Assert.NotNull(active);
    }

    [Fact]
    public async Task RankExhaustionRebalancesAndConcurrentReordersRemainDeterministic()
    {
        var stamp = Guid.NewGuid().ToString("N");
        var organizationId = "domain005-" + stamp;
        var owner = await PostAsync<AuthResponse>("/api/auth/register", new RegisterUserRequest(
            "domain005-owner-" + stamp,
            $"domain005-owner-{stamp}@zumbo.local",
            "P@ssword123",
            organizationId));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", owner.AccessToken);
        await PostAsync<OrganizationResponse>(
            "/api/organizations",
            new CreateOrganizationRequest("Domain 005", organizationId));
        var project = await PostAsync<ProjectResponse>("/api/projects", new CreateProjectRequest(
            organizationId,
            "RK" + stamp[..6],
            "Rank Rebalance",
            owner.User.Id));
        var board = await PostAsync<BoardResponse>("/api/boards", new CreateBoardRequest(
            project.Id,
            "Rank Board",
            "Kanban"));
        _ = await PostAsync<WorkItemResponse>("/api/work-items", new CreateWorkItemRequest(
            project.Id, board.Id, "Predecessor", "Task", "Medium", owner.User.Id, null));
        var anchor = await PostAsync<WorkItemResponse>("/api/work-items", new CreateWorkItemRequest(
            project.Id, board.Id, "Anchor", "Task", "Medium", owner.User.Id, null));
        var movers = new List<WorkItemResponse>();
        for (var index = 0; index < 24; index++)
        {
            movers.Add(await PostAsync<WorkItemResponse>("/api/work-items", new CreateWorkItemRequest(
                project.Id,
                board.Id,
                $"Mover {index:D2}",
                "Task",
                "Medium",
                owner.User.Id,
                null)));
        }

        foreach (var mover in movers.Take(22))
        {
            var response = await client.PatchAsJsonAsync(
                $"/api/work-items/{mover.Id}/rank",
                new ReorderWorkItemRequest(anchor.Id, null));
            response.EnsureSuccessStatusCode();
        }

        var rebalancedAnchor = await GetAsync<WorkItemResponse>($"/api/work-items/{anchor.Id}");
        Assert.True(rebalancedAnchor.Rank > anchor.Rank);
        var concurrent = await Task.WhenAll(movers.Skip(22).Select(mover =>
            client.PatchAsJsonAsync(
                $"/api/work-items/{mover.Id}/rank",
                new ReorderWorkItemRequest(anchor.Id, null))));
        Assert.All(concurrent, response => Assert.Equal(HttpStatusCode.OK, response.StatusCode));

        var ordered = await GetAsync<IReadOnlyList<WorkItemResponse>>(
            $"/api/work-items?projectId={project.Id}&status=To%20Do&page=1&pageSize=100");
        Assert.Equal(26, ordered.Count);
        Assert.Equal(ordered.Count, ordered.Select(item => item.Rank).Distinct().Count());
        Assert.All(
            ordered.Zip(ordered.Skip(1)),
            pair => Assert.True(pair.First.Rank < pair.Second.Rank));
    }

    [Fact]
    public async Task SprintPlanning_StartUniquenessCompletionAndCarryover_AreAtomic()
    {
        var stamp = Guid.NewGuid().ToString("N");
        var organizationId = "domain006-" + stamp;
        var owner = await PostAsync<AuthResponse>("/api/auth/register", new RegisterUserRequest(
            "domain006-owner-" + stamp,
            $"domain006-owner-{stamp}@zumbo.local",
            "P@ssword123",
            organizationId));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", owner.AccessToken);
        await PostAsync<OrganizationResponse>(
            "/api/organizations",
            new CreateOrganizationRequest("Domain 006", organizationId));
        var project = await PostAsync<ProjectResponse>("/api/projects", new CreateProjectRequest(
            organizationId,
            "SP" + stamp[..6],
            "Sprint Lifecycle",
            owner.User.Id));
        var board = await PostAsync<BoardResponse>("/api/boards", new CreateBoardRequest(
            project.Id,
            "Sprint Board",
            "Scrum"));
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var firstSprint = await PostAsync<SprintResponse>("/api/sprints", new CreateSprintRequest(
            project.Id, "Sprint A", "Concurrent planning", today, today.AddDays(6)));
        var secondSprint = await PostAsync<SprintResponse>("/api/sprints", new CreateSprintRequest(
            project.Id, "Sprint B", "Carryover", today.AddDays(7), today.AddDays(13)));
        var firstItem = await PostAsync<WorkItemResponse>("/api/work-items", new CreateWorkItemRequest(
            project.Id, board.Id, "Concurrent item", "Task", "High", owner.User.Id, null));
        var secondItem = await PostAsync<WorkItemResponse>("/api/work-items", new CreateWorkItemRequest(
            project.Id, board.Id, "Committed item", "Task", "Medium", owner.User.Id, null));
        var carryoverItem = await PostAsync<WorkItemResponse>("/api/work-items", new CreateWorkItemRequest(
            project.Id, board.Id, "Next sprint item", "Task", "Low", owner.User.Id, null));
        var backlog = await GetAsync<SprintBacklogPageResponse>($"/api/sprints/projects/{project.Id}/backlog?pageSize=2");
        Assert.Equal(2, backlog.Items.Count);
        Assert.NotNull(backlog.NextCursor);

        using var planFirst = VersionedRequest(
            HttpMethod.Put,
            $"/api/sprints/{firstSprint.Id}/items/{firstItem.Id}",
            new PlanSprintWorkItemRequest(5),
            firstItem.Version);
        using var planSecond = VersionedRequest(
            HttpMethod.Put,
            $"/api/sprints/{secondSprint.Id}/items/{firstItem.Id}",
            new PlanSprintWorkItemRequest(8),
            firstItem.Version);
        var concurrent = await Task.WhenAll(client.SendAsync(planFirst), client.SendAsync(planSecond));
        Assert.Single(concurrent, response => response.StatusCode == HttpStatusCode.OK);
        Assert.Single(concurrent, response => response.StatusCode == HttpStatusCode.Conflict);
        var winningSprint = concurrent[0].StatusCode == HttpStatusCode.OK ? firstSprint : secondSprint;
        var carryoverSprint = winningSprint.Id == firstSprint.Id ? secondSprint : firstSprint;
        var concurrentItemPoints = winningSprint.Id == firstSprint.Id ? 5m : 8m;

        await SendVersionedAsync<SprintPlannedItemResponse>(
            HttpMethod.Put,
            $"/api/sprints/{winningSprint.Id}/items/{secondItem.Id}",
            new PlanSprintWorkItemRequest(3),
            secondItem.Version);
        await SendVersionedAsync<SprintPlannedItemResponse>(
            HttpMethod.Put,
            $"/api/sprints/{carryoverSprint.Id}/items/{carryoverItem.Id}",
            new PlanSprintWorkItemRequest(2),
            carryoverItem.Version);
        var active = await PostAsync<SprintResponse>($"/api/sprints/{winningSprint.Id}/start", new { });
        Assert.Equal(SprintStatuses.Active, active.Status);
        Assert.Equal(2, active.CommittedItems);

        var competingStart = await client.PostAsJsonAsync($"/api/sprints/{carryoverSprint.Id}/start", new { });
        await AssertErrorAsync(competingStart, HttpStatusCode.Conflict, "SPRINT_ACTIVE_EXISTS");
        var activePlanning = await client.PutAsJsonAsync(
            $"/api/sprints/{winningSprint.Id}/items/{carryoverItem.Id}",
            new PlanSprintWorkItemRequest(1));
        await AssertErrorAsync(activePlanning, HttpStatusCode.Conflict, "SPRINT_PLANNING_CLOSED");

        var completed = await PostAsync<SprintResponse>(
            $"/api/sprints/{winningSprint.Id}/complete",
            new CompleteSprintRequest(carryoverSprint.Id));
        Assert.Equal(SprintStatuses.Completed, completed.Status);
        Assert.Equal(0, completed.CompletedItems);
        Assert.Equal(2, completed.CarryoverItems);
        Assert.Equal(concurrentItemPoints + 3m, completed.CarryoverPoints);
        var carriedFirst = await GetAsync<WorkItemResponse>($"/api/work-items/{firstItem.Id}");
        var carriedSecond = await GetAsync<WorkItemResponse>($"/api/work-items/{secondItem.Id}");
        Assert.Equal(carryoverSprint.Id, carriedFirst.SprintId);
        Assert.Equal(carryoverSprint.Id, carriedSecond.SprintId);

        var burndown = await GetAsync<IReadOnlyList<SprintBurndownPointResponse>>(
            $"/api/sprints/{winningSprint.Id}/burndown");
        Assert.All(burndown, point => Assert.Equal(2, point.RemainingItems));
        var velocity = await GetAsync<IReadOnlyList<SprintVelocityResponse>>(
            $"/api/sprints/projects/{project.Id}/velocity?sprintCount=12");
        Assert.Contains(velocity, item => item.SprintId == winningSprint.Id && item.CompletedItems == 0);
        var audit = await EventuallyAsync(
            () => GetAsync<IReadOnlyList<AuditLogResponse>>($"/api/audit/entity/Sprint/{winningSprint.Id}"),
            value => value.Any(item => item.Action == "SprintStarted")
                && value.Any(item => item.Action == "SprintCompleted"),
            "Sprint audit events were not consumed.");
        Assert.Contains(audit, item => item.Action == "SprintStarted");
        Assert.Contains(audit, item => item.Action == "SprintCompleted");

        var nextActive = await PostAsync<SprintResponse>($"/api/sprints/{carryoverSprint.Id}/start", new { });
        Assert.Equal(SprintStatuses.Active, nextActive.Status);
        Assert.Equal(3, nextActive.CommittedItems);
        Assert.Equal(concurrentItemPoints + 5m, nextActive.CommittedPoints);
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
        object? body,
        long expectedVersion)
    {
        using var request = VersionedRequest(method, url, body, expectedVersion);
        var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ApiResponse<T>>())!.Data!;
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

    private static async Task AssertErrorAsync(
        HttpResponseMessage response,
        HttpStatusCode status,
        string code)
    {
        Assert.Equal(status, response.StatusCode);
        var envelope = await response.Content.ReadFromJsonAsync<ApiResponse<object>>();
        Assert.Equal(code, envelope!.Error!.Code);
    }

    private static async Task<T> EventuallyAsync<T>(
        Func<Task<T>> read,
        Func<T, bool> condition,
        string failureMessage)
    {
        T? latest = default;
        for (var attempt = 0; attempt < 200; attempt++)
        {
            latest = await read();
            if (condition(latest))
            {
                return latest;
            }

            await Task.Delay(25);
        }

        throw new Xunit.Sdk.XunitException(failureMessage);
    }
}
