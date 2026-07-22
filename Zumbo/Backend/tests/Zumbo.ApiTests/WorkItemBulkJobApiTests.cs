using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Zumbo.Modules.Audit;
using Zumbo.Modules.Boards;
using Zumbo.Modules.Identity;
using Zumbo.Modules.Organizations;
using Zumbo.Modules.Projects;
using Zumbo.Modules.WorkItems;
using Zumbo.SharedKernel;

namespace Zumbo.ApiTests;

public sealed class WorkItemBulkJobApiTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient client;

    public WorkItemBulkJobApiTests(WebApplicationFactory<Program> factory)
    {
        client = factory.WithWebHostBuilder(builder => builder.ConfigureAppConfiguration((_, configuration) =>
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["WorkItemBulkJobs:BatchSize"] = "2",
                ["WorkItemBulkJobs:MaxInputItems"] = "100",
                ["DurableMessaging:IdleDelay"] = "00:00:01"
            }))).CreateClient();
    }

    [Fact]
    public async Task ImportExportAndBulkJobs_AreDryRunIdempotentResumableTenantSafeAndBounded()
    {
        var stamp = Guid.NewGuid().ToString("N");
        var tenant = "domain010-" + stamp;
        var owner = await RegisterAsync("domain010-owner-" + stamp, tenant);
        var outsider = await RegisterAsync("domain010-outsider-" + stamp, "foreign-" + stamp);
        Authorize(owner);
        await PostAsync<OrganizationResponse>("/api/organizations", new CreateOrganizationRequest("Domain 010", tenant));
        var project = await PostAsync<ProjectResponse>("/api/projects",
            new CreateProjectRequest(tenant, "J" + stamp[..7], "Bulk jobs", owner.User.Id));
        var board = await PostAsync<BoardResponse>("/api/boards",
            new CreateBoardRequest(project.Id, "Bulk board", "Kanban"));

        var dryRequest = new CreateWorkItemImportJobRequest(project.Id,
        [
            new WorkItemImportRow("valid", board.Id, "Dry-run item", "Task", "Medium", null, null),
            new WorkItemImportRow("bad-board", "missing-board", "Invalid dry-run item", "Task", "Medium", null, null)
        ], DryRun: true);
        var dry = await SubmitJobAsync("/api/work-items/bulk/jobs/import", dryRequest, "dry-" + stamp);
        var duplicate = await SubmitJobAsync("/api/work-items/bulk/jobs/import", dryRequest, "dry-" + stamp);
        Assert.Equal(dry.Id, duplicate.Id);
        dry = await AwaitTerminalAsync(dry.Id);
        Assert.Equal(WorkItemBulkJobStates.CompletedWithErrors, dry.State);
        Assert.Equal(1, dry.SucceededItems);
        Assert.Equal(1, dry.FailedItems);
        Assert.True(dry.HasResult);
        Assert.True(dry.HasErrorFile);
        var dryErrors = await client.GetStringAsync($"/api/work-items/bulk/jobs/{dry.Id}/errors");
        Assert.Contains("bad-board", dryErrors, StringComparison.Ordinal);

        var changedRequest = dryRequest with
        {
            Items = [new WorkItemImportRow("changed", board.Id, "Changed", "Task", "Medium", null, null)]
        };
        await AssertErrorAsync(
            await SendJobAsync("/api/work-items/bulk/jobs/import", changedRequest, "dry-" + stamp),
            HttpStatusCode.Conflict, "IDEMPOTENCY_KEY_REUSED");
        var drySearch = await GetAsync<IReadOnlyList<WorkItemResponse>>(
            $"/api/work-items?projectId={project.Id}&text=Dry-run&page=1&pageSize=20");
        Assert.Empty(drySearch);

        var imported = await SubmitJobAsync("/api/work-items/bulk/jobs/import",
            new CreateWorkItemImportJobRequest(project.Id,
            [
                new WorkItemImportRow("one", board.Id, "Imported one", "Task", "High", owner.User.Id, null),
                new WorkItemImportRow("two", board.Id, "Imported two", "Task", "Low", null, null)
            ]), "import-" + stamp);
        imported = await AwaitTerminalAsync(imported.Id);
        Assert.Equal(WorkItemBulkJobStates.Completed, imported.State);
        Assert.Equal(2, imported.SucceededItems);
        var importResult = await client.GetStringAsync($"/api/work-items/bulk/jobs/{imported.Id}/result");
        Assert.Contains("\"sourceKey\":\"one\"", importResult, StringComparison.Ordinal);
        var importedSearch = await AwaitWorkItemSearchAsync(project.Id, "Imported", 2);
        Assert.Equal(2, importedSearch.Count);

        var validId = importedSearch.First().Id;
        var partial = await SubmitJobAsync("/api/work-items/bulk/jobs",
            new CreateWorkItemBulkJobRequest(project.Id, WorkItemBulkOperations.Move,
                [validId, "missing-work-item"], "In Progress"), "partial-" + stamp);
        partial = await AwaitTerminalAsync(partial.Id);
        Assert.Equal(WorkItemBulkJobStates.CompletedWithErrors, partial.State);
        Assert.Equal(1, partial.SucceededItems);
        Assert.Contains("WORK_ITEM_NOT_FOUND",
            await client.GetStringAsync($"/api/work-items/bulk/jobs/{partial.Id}/errors"),
            StringComparison.Ordinal);
        var retried = await PostAsync<WorkItemBulkJobResponse>(
            $"/api/work-items/bulk/jobs/{partial.Id}/retry", new { });
        Assert.Equal(WorkItemBulkJobStates.Pending, retried.State);
        retried = await AwaitTerminalAsync(retried.Id);
        Assert.Equal(WorkItemBulkJobStates.CompletedWithErrors, retried.State);

        var exportDryRun = await SubmitJobAsync("/api/work-items/bulk/jobs/export",
            new CreateWorkItemExportJobRequest(project.Id, DryRun: true), "export-dry-" + stamp);
        exportDryRun = await AwaitTerminalAsync(exportDryRun.Id);
        Assert.Equal(WorkItemBulkJobStates.Completed, exportDryRun.State);
        Assert.True(exportDryRun.DryRun);
        Assert.Equal(importedSearch.Count, exportDryRun.SucceededItems);

        var export = await SubmitJobAsync("/api/work-items/bulk/jobs/export",
            new CreateWorkItemExportJobRequest(project.Id), "export-" + stamp);
        export = await AwaitTerminalAsync(export.Id);
        Assert.Equal(WorkItemBulkJobStates.Completed, export.State);
        Assert.Contains("Imported one",
            await client.GetStringAsync($"/api/work-items/bulk/jobs/{export.Id}/result"),
            StringComparison.Ordinal);

        var cancellable = await SubmitJobAsync("/api/work-items/bulk/jobs",
            new CreateWorkItemBulkJobRequest(project.Id, WorkItemBulkOperations.Archive,
                Enumerable.Range(0, 80).Select(index => "missing-" + index).ToArray()),
            "cancel-" + stamp);
        var cancelled = await PostAsync<WorkItemBulkJobResponse>(
            $"/api/work-items/bulk/jobs/{cancellable.Id}/cancel", new { });
        if (cancelled.State != WorkItemBulkJobStates.Cancelled)
            cancelled = await AwaitTerminalAsync(cancelled.Id);
        Assert.Equal(WorkItemBulkJobStates.Cancelled, cancelled.State);

        Authorize(outsider);
        await AssertErrorAsync(await client.GetAsync($"/api/work-items/bulk/jobs/{imported.Id}"),
            HttpStatusCode.NotFound, "WORK_ITEM_BULK_JOB_NOT_FOUND");
        Authorize(owner);
        var tooLarge = new CreateWorkItemBulkJobRequest(project.Id, WorkItemBulkOperations.Archive,
            Enumerable.Range(0, 101).Select(index => "large-" + index).ToArray());
        await AssertErrorAsync(await SendJobAsync("/api/work-items/bulk/jobs", tooLarge, "large-" + stamp),
            HttpStatusCode.BadRequest, "VALIDATION_ERROR");

        var privacy = await GetAsync<PrivacyExportResponse>("/api/auth/privacy/export");
        Assert.Contains(privacy.Data, group => group.Category == "work-item-bulk-jobs" && group.Items.Count >= 4);
        var audit = await AwaitAuditAsync(imported.Id);
        Assert.Contains(audit, entry => entry.Action == "WorkItemBulkJobCreated");
        Assert.Contains(audit, entry => entry.Action == "WorkItemBulkJobCompleted");
    }

    private async Task<WorkItemBulkJobResponse> AwaitTerminalAsync(string id)
    {
        for (var attempt = 0; attempt < 400; attempt++)
        {
            var job = await GetAsync<WorkItemBulkJobResponse>($"/api/work-items/bulk/jobs/{id}");
            if (WorkItemBulkJobStates.IsTerminal(job.State) || job.State == WorkItemBulkJobStates.Failed) return job;
            await Task.Delay(25);
        }
        throw new Xunit.Sdk.XunitException("Bulk job did not reach a terminal state within the bounded wait.");
    }

    private async Task<IReadOnlyList<WorkItemResponse>> AwaitWorkItemSearchAsync(
        string projectId, string text, int expectedCount)
    {
        IReadOnlyList<WorkItemResponse> result = [];
        for (var attempt = 0; attempt < 200; attempt++)
        {
            result = await GetAsync<IReadOnlyList<WorkItemResponse>>(
                $"/api/work-items?projectId={projectId}&text={Uri.EscapeDataString(text)}&page=1&pageSize=20");
            if (result.Count == expectedCount) return result;
            await Task.Delay(25);
        }
        throw new Xunit.Sdk.XunitException(
            $"Work-item search did not reach {expectedCount} results; observed {result.Count}.");
    }

    private async Task<IReadOnlyList<AuditLogResponse>> AwaitAuditAsync(string jobId)
    {
        for (var attempt = 0; attempt < 200; attempt++)
        {
            var audit = await GetAsync<IReadOnlyList<AuditLogResponse>>(
                $"/api/audit/entity/WorkItemBulkJob/{jobId}");
            if (audit.Any(entry => entry.Action == "WorkItemBulkJobCompleted")) return audit;
            await Task.Delay(25);
        }
        throw new Xunit.Sdk.XunitException("Bulk job audit was not visible within the bounded wait.");
    }

    private async Task<WorkItemBulkJobResponse> SubmitJobAsync(string url, object body, string key)
    {
        var response = await SendJobAsync(url, body, key);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ApiResponse<WorkItemBulkJobResponse>>())!.Data!;
    }

    private async Task<HttpResponseMessage> SendJobAsync(string url, object body, string key)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = JsonContent.Create(body) };
        request.Headers.TryAddWithoutValidation("Idempotency-Key", key);
        return await client.SendAsync(request);
    }

    private async Task<AuthResponse> RegisterAsync(string username, string organizationId) =>
        await PostAsync<AuthResponse>("/api/auth/register",
            new RegisterUserRequest(username, username + "@zumbo.local", "P@ssword123", organizationId));
    private void Authorize(AuthResponse auth) =>
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);
    private async Task<T> PostAsync<T>(string url, object body)
    {
        var response = await client.PostAsJsonAsync(url, body);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ApiResponse<T>>())!.Data!;
    }
    private async Task<T> GetAsync<T>(string url)
    {
        var response = await client.GetAsync(url);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ApiResponse<T>>())!.Data!;
    }
    private static async Task AssertErrorAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        Assert.Equal(status, response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<ApiResponse<JsonElement>>();
        Assert.Equal(code, error!.Error!.Code);
    }
}
