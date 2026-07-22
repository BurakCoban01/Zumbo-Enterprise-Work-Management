using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Zumbo.Modules.Boards;
using Zumbo.Modules.Identity;
using Zumbo.Modules.Notifications;
using Zumbo.Modules.Organizations;
using Zumbo.Modules.Projects;
using Zumbo.Modules.WorkItems;
using Zumbo.SharedKernel;

namespace Zumbo.ApiTests;

public sealed class WorkItemCollaborationRecurrenceApiTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient client;

    public WorkItemCollaborationRecurrenceApiTests(WebApplicationFactory<Program> factory)
    {
        client = factory.WithWebHostBuilder(builder => builder.ConfigureAppConfiguration((_, configuration) =>
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["WorkItemRecurrence:IntervalSeconds"] = "5",
                ["WorkItemRecurrence:BatchSize"] = "20"
            }))).CreateClient();
    }

    [Fact]
    public async Task CollaborationTemplateAndRecurrence_LifecycleIsTenantSafeDurableAndIdempotent()
    {
        var stamp = Guid.NewGuid().ToString("N");
        var organizationId = "domain009-" + stamp;
        var owner = await RegisterAsync("domain009-owner-" + stamp, organizationId);
        var collaborator = await RegisterAsync("domain009-collaborator-" + stamp, organizationId);
        var outsider = await RegisterAsync("domain009-outsider-" + stamp, "domain009-foreign-" + stamp);

        Authorize(owner);
        await PostAsync<OrganizationResponse>(
            "/api/organizations",
            new CreateOrganizationRequest("Domain 009", organizationId));
        var project = await PostAsync<ProjectResponse>(
            "/api/projects",
            new CreateProjectRequest(organizationId, "D" + stamp[..7], "Collaboration", owner.User.Id));
        await PostAsync<ProjectResponse>(
            $"/api/projects/{project.Id}/members",
            new AddProjectMemberRequest(collaborator.User.Id, ProjectRoles.Developer));
        var board = await PostAsync<BoardResponse>(
            "/api/boards",
            new CreateBoardRequest(project.Id, "Delivery", "Kanban"));
        var item = await PostAsync<WorkItemResponse>(
            "/api/work-items",
            new CreateWorkItemRequest(project.Id, board.Id, "Collaborative task", "Task", "High", owner.User.Id, null));

        Authorize(collaborator);
        var watching = await PutAsync<WorkItemCollaborationResponse>(
            $"/api/work-items/{item.Id}/watch",
            new SetWorkItemWatchRequest(true));
        Assert.True(watching.Watching);
        Assert.Equal(1, watching.WatcherCount);
        var duplicateWatch = await client.PutAsJsonAsync(
            $"/api/work-items/{item.Id}/watch",
            new SetWorkItemWatchRequest(true));
        await AssertErrorAsync(duplicateWatch, HttpStatusCode.Conflict, "WORK_ITEM_WATCH_UNCHANGED");

        var voted = await PutAsync<WorkItemCollaborationResponse>(
            $"/api/work-items/{item.Id}/vote",
            new SetWorkItemVoteRequest(true));
        Assert.True(voted.Voted);
        Assert.Equal(1, voted.VoteCount);
        await PutAsync<NotificationPreferenceResponse>(
            "/api/notifications/preferences/me",
            new UpdateNotificationPreferencesRequest(
                true,
                false,
                ["WatcherUpdate"],
                [new NotificationTypePreferenceRequest("Mention", true, false)]));

        Authorize(owner);
        var commented = await PostAsync<WorkItemResponse>(
            $"/api/work-items/{item.Id}/comments",
            new AddCommentRequest("Please review the bounded recurrence.", [collaborator.User.Id]));
        _ = await PutAsync<WorkItemResponse>(
            $"/api/work-items/{item.Id}",
            new UpdateWorkItemRequest("Collaborative task updated", null, "High", null));
        var checklist = await PostAsync<WorkItemResponse>(
            $"/api/work-items/{item.Id}/checklist",
            new AddChecklistItemRequest("Verify recurrence output"));
        _ = await PatchAsync<WorkItemResponse>(
            $"/api/work-items/{item.Id}/checklist/{checklist.Checklist.Single().Id}",
            new CompleteChecklistItemRequest(true));
        _ = await PostAsync<WorkItemResponse>(
            $"/api/work-items/{item.Id}/labels",
            new AddLabelRequest("collaboration"));
        (await client.DeleteAsync($"/api/work-items/{item.Id}/labels/collaboration"))
            .EnsureSuccessStatusCode();
        _ = await PatchAsync<WorkItemResponse>(
            $"/api/work-items/{item.Id}/planning",
            new SetWorkItemPlanningRequest(null, 3));
        _ = await PutAsync<WorkItemResponse>(
            $"/api/work-items/{item.Id}/comments/{commented.Comments.Single().Id}",
            new EditCommentRequest("Please review the bounded recurrence output."));

        Authorize(collaborator);
        var mention = await EventuallyAsync(async () =>
        {
            var notifications = await GetAsync<IReadOnlyCollection<NotificationResponse>>(
                "/api/notifications?page=1&pageSize=50");
            return notifications.SingleOrDefault(notification => notification.Type == "Mention");
        });
        Assert.DoesNotContain(
            await GetAsync<IReadOnlyCollection<NotificationResponse>>("/api/notifications?page=1&pageSize=50"),
            notification => notification.Type == "WatcherUpdate");
        Assert.Contains("Mentioned on", mention.Message, StringComparison.Ordinal);

        var activity = await GetAsync<WorkItemEventActivityPage>(
            $"/api/work-items/{item.Id}/activity?page=1&pageSize=20");
        Assert.Contains(activity.Items, entry => entry.Type == "WorkItemWatched");
        Assert.Contains(activity.Items, entry => entry.Type == "WorkItemVoted");
        Assert.Contains(activity.Items, entry => entry.Type == "WorkItemCreated");
        Assert.Contains(activity.Items, entry => entry.Type == "WorkItemUpdated");
        Assert.Contains(activity.Items, entry => entry.Type == "WorkItemCommentAdded");
        Assert.Contains(activity.Items, entry => entry.Type == "WorkItemCommentEdited");
        Assert.Contains(activity.Items, entry => entry.Type == "WorkItemChecklistItemAdded");
        Assert.Contains(activity.Items, entry => entry.Type == "WorkItemChecklistItemUpdated");
        Assert.Contains(activity.Items, entry => entry.Type == "WorkItemLabelAdded");
        Assert.Contains(activity.Items, entry => entry.Type == "WorkItemLabelRemoved");
        Assert.Contains(activity.Items, entry => entry.Type == "WorkItemPlanningUpdated");
        Assert.DoesNotContain(activity.Items, entry => entry.Detail == collaborator.User.Id);

        Authorize(outsider);
        var hidden = await client.GetAsync($"/api/work-items/{item.Id}/collaboration");
        Assert.Equal(HttpStatusCode.NotFound, hidden.StatusCode);

        Authorize(owner);
        var template = await PostAsync<WorkItemTemplateResponse>(
            "/api/work-items/templates",
            new CreateWorkItemTemplateRequest(
                project.Id,
                board.Id,
                "Daily review",
                "Daily operational review",
                "Review current delivery risks.",
                "Task",
                "Medium",
                collaborator.User.Id,
                null,
                1,
                ["operations"],
                []));
        template = await PutAsync<WorkItemTemplateResponse>(
            $"/api/work-items/templates/{template.Id}",
            new UpdateWorkItemTemplateRequest(
                board.Id,
                "Daily review",
                "Daily operational review updated",
                "Review current delivery risks and recurrence output.",
                "Task",
                "Medium",
                collaborator.User.Id,
                null,
                1,
                ["operations"],
                []));
        var lifecycleRecurrence = await PostAsync<WorkItemRecurrenceResponse>(
            "/api/work-items/recurrences",
            new CreateWorkItemRecurrenceRequest(
                project.Id,
                template.Id,
                WorkItemRecurrenceFrequencies.Weekly,
                1,
                DateTimeOffset.UtcNow.AddDays(1),
                DateTimeOffset.UtcNow.AddDays(15),
                2));
        var pausedRecurrence = await PatchAsync<WorkItemRecurrenceResponse>(
            $"/api/work-items/recurrences/{lifecycleRecurrence.Id}/state",
            new SetWorkItemRecurrenceStateRequest(false));
        Assert.False(pausedRecurrence.Active);
        var resumedRecurrence = await PatchAsync<WorkItemRecurrenceResponse>(
            $"/api/work-items/recurrences/{lifecycleRecurrence.Id}/state",
            new SetWorkItemRecurrenceStateRequest(true));
        Assert.True(resumedRecurrence.Active);
        using (var archiveLifecycleRecurrence = new HttpRequestMessage(
                   HttpMethod.Delete,
                   $"/api/work-items/recurrences/{lifecycleRecurrence.Id}"))
        {
            archiveLifecycleRecurrence.Headers.TryAddWithoutValidation(
                "If-Match", $"\"{resumedRecurrence.Version}\"");
            Assert.Equal(
                HttpStatusCode.NoContent,
                (await client.SendAsync(archiveLifecycleRecurrence)).StatusCode);
        }
        var recurrence = await PostAsync<WorkItemRecurrenceResponse>(
            "/api/work-items/recurrences",
            new CreateWorkItemRecurrenceRequest(
                project.Id,
                template.Id,
                WorkItemRecurrenceFrequencies.Daily,
                1,
                DateTimeOffset.UtcNow,
                DateTimeOffset.UtcNow.AddDays(2),
                1));

        var generatedOccurrence = await EventuallyAsync(async () =>
        {
            var page = await GetAsync<WorkItemRecurrenceOccurrencePage>(
                $"/api/work-items/recurrences/{recurrence.Id}/occurrences?page=1&pageSize=10");
            return page.Items.SingleOrDefault(entry =>
                entry.Status == WorkItemRecurrenceOccurrenceStates.Generated
                && entry.CreatedWorkItemId is not null);
        }, attempts: 400, delayMilliseconds: 25);
        var generated = await GetAsync<WorkItemResponse>(
            "/api/work-items/" + generatedOccurrence.CreatedWorkItemId);
        Assert.Equal(template.Title, generated.Title);
        Assert.Equal(template.AssigneeUserId, generated.AssigneeUserId);
        Assert.Equal(template.Labels, generated.Labels);

        await Task.Delay(250);
        var occurrences = await GetAsync<WorkItemRecurrenceOccurrencePage>(
            $"/api/work-items/recurrences/{recurrence.Id}/occurrences?page=1&pageSize=10");
        Assert.Single(occurrences.Items);
        var recurrences = await GetAsync<WorkItemRecurrencePage>(
            $"/api/work-items/recurrences?projectId={project.Id}&page=1&pageSize=10");
        var completedRecurrence = Assert.Single(recurrences.Items);
        Assert.False(completedRecurrence.Active);
        Assert.Equal(1, completedRecurrence.ScheduledOccurrences);
        Assert.Equal(1, completedRecurrence.GeneratedOccurrences);

        Authorize(collaborator);
        var privacyExport = await GetAsync<PrivacyExportResponse>("/api/auth/privacy/export");
        Assert.Contains(privacyExport.Data, group =>
            group.Category == "work-item-collaboration" && group.Items.Count > 0);
        Assert.Contains(privacyExport.Data, group =>
            group.Category == "work-item-activity" && group.Items.Count > 0);
        var anonymized = await PostAsync<AnonymizeAccountResponse>(
            "/api/auth/privacy/anonymize",
            new AnonymizeAccountRequest("P@ssword123", "ANONYMIZE"));
        Assert.True(anonymized.Anonymized);

        Authorize(owner);
        var sanitizedCollaboration = await GetAsync<WorkItemCollaborationResponse>(
            $"/api/work-items/{item.Id}/collaboration");
        Assert.Equal(0, sanitizedCollaboration.WatcherCount);
        Assert.Equal(0, sanitizedCollaboration.VoteCount);
        var sanitizedActivity = await GetAsync<WorkItemEventActivityPage>(
            $"/api/work-items/{item.Id}/activity?page=1&pageSize=20");
        Assert.DoesNotContain(sanitizedActivity.Items, entry => entry.ActorUserId == collaborator.User.Id);
        var templatePage = await GetAsync<WorkItemTemplatePage>(
            $"/api/work-items/templates?projectId={project.Id}&page=1&pageSize=10");
        var sanitizedTemplate = Assert.Single(templatePage.Items);
        Assert.Null(sanitizedTemplate.AssigneeUserId);

        using (var archiveRecurrence = new HttpRequestMessage(
                   HttpMethod.Delete,
                   $"/api/work-items/recurrences/{recurrence.Id}"))
        {
            archiveRecurrence.Headers.TryAddWithoutValidation("If-Match", $"\"{completedRecurrence.Version}\"");
            Assert.Equal(HttpStatusCode.NoContent, (await client.SendAsync(archiveRecurrence)).StatusCode);
        }
        using (var archiveTemplate = new HttpRequestMessage(
                   HttpMethod.Delete,
                   $"/api/work-items/templates/{template.Id}"))
        {
            archiveTemplate.Headers.TryAddWithoutValidation("If-Match", $"\"{sanitizedTemplate.Version}\"");
            Assert.Equal(HttpStatusCode.NoContent, (await client.SendAsync(archiveTemplate)).StatusCode);
        }
    }

    private async Task<AuthResponse> RegisterAsync(string username, string organizationId) =>
        await PostAsync<AuthResponse>("/api/auth/register", new RegisterUserRequest(
            username,
            username + "@zumbo.local",
            "P@ssword123",
            organizationId));

    private void Authorize(AuthResponse authentication) =>
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", authentication.AccessToken);

    private async Task<T> PostAsync<T>(string url, object request)
    {
        var response = await client.PostAsJsonAsync(url, request);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ApiResponse<T>>())!.Data!;
    }

    private async Task<T> PutAsync<T>(string url, object request)
    {
        var response = await client.PutAsJsonAsync(url, request);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ApiResponse<T>>())!.Data!;
    }

    private async Task<T> PatchAsync<T>(string url, object request)
    {
        var response = await client.PatchAsJsonAsync(url, request);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ApiResponse<T>>())!.Data!;
    }

    private async Task<T> GetAsync<T>(string url)
    {
        var response = await client.GetAsync(url);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ApiResponse<T>>())!.Data!;
    }

    private static async Task AssertErrorAsync(
        HttpResponseMessage response,
        HttpStatusCode expectedStatus,
        string expectedCode)
    {
        Assert.Equal(expectedStatus, response.StatusCode);
        var envelope = await response.Content.ReadFromJsonAsync<ApiResponse<object>>();
        Assert.Equal(expectedCode, envelope!.Error!.Code);
    }

    private static async Task<T> EventuallyAsync<T>(
        Func<Task<T?>> operation,
        int attempts = 200,
        int delayMilliseconds = 25)
        where T : class
    {
        for (var attempt = 0; attempt < attempts; attempt++)
        {
            var result = await operation();
            if (result is not null)
            {
                return result;
            }
            await Task.Delay(delayMilliseconds);
        }
        throw new Xunit.Sdk.XunitException("The durable collaboration result was not visible within the bounded wait.");
    }
}
