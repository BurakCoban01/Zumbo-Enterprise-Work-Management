using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Zumbo.Modules.Audit;
using Zumbo.Modules.Boards;
using Zumbo.Modules.Identity;
using Zumbo.Modules.Notifications;
using Zumbo.Modules.Organizations;
using Zumbo.Modules.Projects;
using Zumbo.Modules.Teams;
using Zumbo.Modules.WorkItems;
using Zumbo.Modules.Workflows;
using Zumbo.SharedKernel;

namespace Zumbo.ApiTests;

public sealed class ApiFlowTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;

    public ApiFlowTests(WebApplicationFactory<Program> factory)
    {
        _client = factory.WithWebHostBuilder(builder => builder.ConfigureAppConfiguration((_, configuration) =>
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["IdentityBootstrap:BootstrapToken"] = "development-bootstrap-token"
            }))).CreateClient();
    }

    [Fact]
    public async Task EndToEnd_ProjectBoardWorkItemFlow_WritesAuditAndNotifications()
    {
        _client.DefaultRequestHeaders.UserAgent.ParseAdd("Zumbo-ApiTests/1.0");
        var register = await PostAsync<AuthResponse>("/api/auth/register", new RegisterUserRequest(
            "api-flow-user",
            "api-flow-user@zumbo.local",
            "P@ssword123",
            "org-1"));

        Assert.False(string.IsNullOrWhiteSpace(register.AccessToken));
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", register.AccessToken);

        var project = await PostAsync<ProjectResponse>("/api/projects", new CreateProjectRequest(
            "org-1",
            "ZUM",
            "Zumbo Platform",
            register.User.Id));
        project = await PutAsync<ProjectResponse>($"/api/projects/{project.Id}", new UpdateProjectRequest(
            "Zumbo Delivery Platform",
            "Internal"));
        Assert.Equal("Zumbo Delivery Platform", project.Name);

        var board = await PostAsync<BoardResponse>("/api/boards", new CreateBoardRequest(
            project.Id,
            "Engineering Board",
            "Kanban"));

        var workItem = await PostAsync<WorkItemResponse>("/api/work-items", new CreateWorkItemRequest(
            project.Id,
            board.Id,
            "Implement auth flow",
            "Task",
            "High",
            register.User.Id,
            DateTimeOffset.UtcNow.AddDays(3)));
        var workItemDetail = await GetAsync<WorkItemResponse>($"/api/work-items/{workItem.Id}");
        Assert.Equal(workItem.Id, workItemDetail.Id);
        Assert.Equal(board.Columns.Single(x => x.Category == "Todo").Id, workItem.ColumnId);
        workItem = await PatchAsync<WorkItemResponse>($"/api/work-items/{workItem.Id}/planning", new SetWorkItemPlanningRequest(
            "sprint-1",
            8));
        Assert.Equal("sprint-1", workItem.SprintId);
        Assert.Equal(8, workItem.EstimatePoints);

        var secondWorkItem = await PostAsync<WorkItemResponse>("/api/work-items", new CreateWorkItemRequest(
            project.Id,
            board.Id,
            "Verify persisted rank",
            "Task",
            "Medium",
            register.User.Id,
            null));
        Assert.True(secondWorkItem.Rank > workItem.Rank);
        secondWorkItem = await PatchAsync<WorkItemResponse>(
            $"/api/work-items/{secondWorkItem.Id}/rank",
            new ReorderWorkItemRequest(workItem.Id, null));
        var firstPage = await GetAsync<IReadOnlyList<WorkItemResponse>>(
            $"/api/work-items?projectId={project.Id}&page=1&pageSize=1");
        Assert.Single(firstPage);
        Assert.Equal(secondWorkItem.Id, firstPage[0].Id);
        var archiveSecond = await _client.DeleteAsync($"/api/work-items/{secondWorkItem.Id}");
        archiveSecond.EnsureSuccessStatusCode();

        workItem = await PostAsync<WorkItemResponse>($"/api/work-items/{workItem.Id}/comments", new AddCommentRequest(
            "Initial implementation note",
            []));
        var commentId = workItem.Comments.Single().Id;
        workItem = await PutAsync<WorkItemResponse>($"/api/work-items/{workItem.Id}/comments/{commentId}", new EditCommentRequest(
            "Updated implementation note"));
        Assert.Contains(workItem.Comments, x => x.Id == commentId && x.Body == "Updated implementation note");
        var editedComment = workItem.Comments.Single(x => x.Id == commentId);
        Assert.NotNull(editedComment.EditedAt);
        Assert.Contains(editedComment.History, x =>
            x.Body == "Initial implementation note" && x.EditedByUserId == register.User.Id);
        workItem = await DeleteAsync<WorkItemResponse>($"/api/work-items/{workItem.Id}/comments/{commentId}");
        Assert.Empty(workItem.Comments);

        using (var invalidMultipart = new MultipartFormDataContent())
        {
            var executableContent = new ByteArrayContent([0x4D, 0x5A, 0x90, 0x00]);
            executableContent.Headers.ContentType = new MediaTypeHeaderValue("image/png");
            invalidMultipart.Add(executableContent, "file", "spoofed.png");
            var invalidUpload = await _client.PostAsync(
                $"/api/work-items/{workItem.Id}/attachments/upload",
                invalidMultipart);
            Assert.Equal(HttpStatusCode.BadRequest, invalidUpload.StatusCode);
            var invalidBody = await invalidUpload.Content.ReadFromJsonAsync<ApiResponse<object>>();
            Assert.Equal("VALIDATION_ERROR", invalidBody!.Error!.Code);
        }

        var attachmentBytes = "Zumbo attachment lifecycle"u8.ToArray();
        using var multipart = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(attachmentBytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("text/plain");
        multipart.Add(fileContent, "file", "notes.txt");
        var uploadResponse = await _client.PostAsync($"/api/work-items/{workItem.Id}/attachments/upload", multipart);
        uploadResponse.EnsureSuccessStatusCode();
        var uploadBody = await uploadResponse.Content.ReadFromJsonAsync<ApiResponse<WorkItemResponse>>();
        workItem = uploadBody!.Data!;
        var attachmentId = workItem.Attachments.Single().Id;
        var preview = await _client.GetAsync($"/api/work-items/{workItem.Id}/attachments/{attachmentId}/preview");
        preview.EnsureSuccessStatusCode();
        Assert.Equal("text/plain", preview.Content.Headers.ContentType!.MediaType);
        Assert.Equal(attachmentBytes, await preview.Content.ReadAsByteArrayAsync());
        var download = await _client.GetAsync($"/api/work-items/{workItem.Id}/attachments/{attachmentId}/download");
        download.EnsureSuccessStatusCode();
        Assert.Equal("notes.txt", download.Content.Headers.ContentDisposition!.FileNameStar);
        Assert.Equal(attachmentBytes, await download.Content.ReadAsByteArrayAsync());
        workItem = await DeleteAsync<WorkItemResponse>($"/api/work-items/{workItem.Id}/attachments/{attachmentId}");
        Assert.Empty(workItem.Attachments);
        var deletedDownload = await _client.GetAsync($"/api/work-items/{workItem.Id}/attachments/{attachmentId}/download");
        Assert.Equal(HttpStatusCode.NotFound, deletedDownload.StatusCode);

        var workflow = await GetAsync<WorkflowResponse>($"/api/workflows/{project.Id}");
        Assert.Contains(workflow.Transitions, x => x.FromStatus == "Test" && x.ToStatus == "Done" && x.RequiresCompletedChecklist);

        workItem = await PatchAsync<WorkItemResponse>($"/api/work-items/{workItem.Id}/status", new MoveWorkItemRequest("In Progress"));
        workItem = await PatchAsync<WorkItemResponse>($"/api/work-items/{workItem.Id}/status", new MoveWorkItemRequest("Code Review"));
        workItem = await PatchAsync<WorkItemResponse>($"/api/work-items/{workItem.Id}/status", new MoveWorkItemRequest("Test"));
        workItem = await PatchAsync<WorkItemResponse>($"/api/work-items/{workItem.Id}/status", new MoveWorkItemRequest("Done"));

        var audit = await GetAsync<IReadOnlyList<object>>($"/api/audit/entity/WorkItem/{workItem.Id}");
        var auditFrom = Uri.EscapeDataString(DateTimeOffset.UtcNow.AddDays(-1).ToString("O"));
        var auditTo = Uri.EscapeDataString(DateTimeOffset.UtcNow.AddDays(1).ToString("O"));
        var filteredAudit = await GetAsync<AuditLogPageResponse>(
            $"/api/audit?entityType=WorkItem&entityId={workItem.Id}&action=WorkItemMoved&from={auditFrom}&to={auditTo}&page=1&pageSize=2");
        var ownAudit = await GetAsync<AuditLogPageResponse>(
            $"/api/audit?actorUserId={register.User.Id}&page=1&pageSize=2");
        var notifications = await GetAsync<IReadOnlyList<object>>($"/api/notifications/{register.User.Id}");
        var summary = await GetAsync<ProjectSummaryResponse>($"/api/work-items/reports/project-summary/{project.Id}");
        var statusDistribution = await GetAsync<IReadOnlyList<StatusDistributionResponse>>($"/api/work-items/reports/status-distribution/{project.Id}");
        var workload = await GetAsync<IReadOnlyList<UserWorkloadResponse>>($"/api/work-items/reports/user-workload/{project.Id}");
        var start = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-1).ToString("yyyy-MM-dd");
        var end = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(1).ToString("yyyy-MM-dd");
        var burndown = await GetAsync<IReadOnlyList<SprintBurndownPointResponse>>(
            $"/api/work-items/reports/sprint-burndown/{project.Id}/sprint-1?startDate={start}&endDate={end}");
        var velocity = await GetAsync<IReadOnlyList<SprintVelocityResponse>>(
            $"/api/work-items/reports/sprint-velocity/{project.Id}?sprintCount=3");
        var flowTime = await GetAsync<FlowTimeReportResponse>(
            $"/api/work-items/reports/flow-time/{project.Id}?from={start}&to={end}");

        Assert.Equal("Done", workItem.Status);
        Assert.NotNull(workItem.CompletedAt);
        Assert.Equal(5, workItem.StatusHistory.Count);
        Assert.Equal("To Do", workItem.StatusHistory.First().ToStatus);
        Assert.Equal("Done", workItem.StatusHistory.Last().ToStatus);
        Assert.NotEmpty(audit);
        Assert.Equal(2, filteredAudit.Items.Count);
        Assert.All(filteredAudit.Items, x => Assert.Equal("WorkItemMoved", x.Action));
        Assert.All(filteredAudit.Items, x => Assert.Equal("Zumbo-ApiTests/1.0", x.UserAgent));
        Assert.True(filteredAudit.HasNextPage);
        Assert.Equal(2, ownAudit.Items.Count);
        Assert.True(ownAudit.HasNextPage);
        Assert.NotEmpty(notifications);
        Assert.Equal(1, summary.Total);
        Assert.Equal(1, summary.Done);
        Assert.Contains(statusDistribution, x => x.Status == "Done" && x.Count == 1);
        Assert.Contains(workload, x => x.UserId == register.User.Id);
        Assert.Contains(burndown, x => x.RemainingPoints == 0 && x.RemainingItems == 0);
        Assert.Contains(velocity, x => x.SprintId == "sprint-1" && x.CompletedItems == 1 && x.CompletedPoints == 8);
        Assert.Equal(1, flowTime.CompletedItems);
        Assert.Equal(1, flowTime.CycleTimeSampleSize);
        Assert.True(flowTime.AverageLeadTimeHours >= 0);
        Assert.True(flowTime.AverageCycleTimeHours >= 0);
    }

    [Fact]
    public async Task ProjectViewer_CannotCreateWorkItem()
    {
        var owner = await PostAsync<AuthResponse>("/api/auth/register", new RegisterUserRequest(
            "owner",
            "owner@zumbo.local",
            "P@ssword123",
            "org-2"));
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", owner.AccessToken);

        var viewer = await PostAsync<AuthResponse>("/api/auth/register", new RegisterUserRequest(
            "viewer",
            "viewer@zumbo.local",
            "P@ssword123",
            "org-2"));

        var project = await PostAsync<ProjectResponse>("/api/projects", new CreateProjectRequest(
            "org-2",
            "SEC",
            "Security Project",
            owner.User.Id));
        var foreignMember = await PostAsync<AuthResponse>("/api/auth/register", new RegisterUserRequest(
            "foreign-member",
            "foreign-member@zumbo.local",
            "P@ssword123",
            "org-foreign"));
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", foreignMember.AccessToken);
        var tenantScopedUsers = await GetAsync<IReadOnlyList<UserProfileResponse>>(
            "/api/auth/users?search=owner%40zumbo.local");
        Assert.Empty(tenantScopedUsers);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", owner.AccessToken);
        var organizationMismatch = await _client.PostAsJsonAsync(
            $"/api/projects/{project.Id}/members",
            new AddProjectMemberRequest(foreignMember.User.Id, "Developer"));
        Assert.Equal(HttpStatusCode.Conflict, organizationMismatch.StatusCode);
        await PostAsync<ProjectResponse>($"/api/projects/{project.Id}/members", new AddProjectMemberRequest(viewer.User.Id, "Developer"));
        var roleUpdatedProject = await PatchAsync<ProjectResponse>(
            $"/api/projects/{project.Id}/members/{viewer.User.Id}/role",
            new ChangeProjectMemberRoleRequest("Viewer"));
        Assert.Contains(roleUpdatedProject.Members, x => x.UserId == viewer.User.Id && x.Role == "Viewer");
        var board = await PostAsync<BoardResponse>("/api/boards", new CreateBoardRequest(project.Id, "Security Board", "Kanban"));
        var auditedWorkItem = await PostAsync<WorkItemResponse>("/api/work-items", new CreateWorkItemRequest(
            project.Id,
            board.Id,
            "Visible audit trail",
            "Task",
            "Medium",
            owner.User.Id,
            null));

        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", viewer.AccessToken);
        var response = await _client.PostAsJsonAsync("/api/work-items", new CreateWorkItemRequest(
            project.Id,
            board.Id,
            "Unauthorized create",
            "Task",
            "Low",
            viewer.User.Id,
            null));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<ApiResponse<object>>();
        Assert.False(body!.Success);
        Assert.Equal("FORBIDDEN", body.Error!.Code);

        var forbiddenProjectUpdate = await _client.PutAsJsonAsync(
            $"/api/projects/{project.Id}",
            new UpdateProjectRequest("Viewer cannot rename", "Internal"));
        Assert.Equal(HttpStatusCode.Forbidden, forbiddenProjectUpdate.StatusCode);
        var spoofedOwner = await _client.PostAsJsonAsync("/api/projects", new CreateProjectRequest(
            "org-2",
            "SPF",
            "Spoofed project",
            owner.User.Id));
        Assert.Equal(HttpStatusCode.Forbidden, spoofedOwner.StatusCode);

        var report = await _client.GetAsync($"/api/work-items/reports/project-summary/{project.Id}");
        Assert.Equal(HttpStatusCode.OK, report.StatusCode);
        var visibleBoards = await _client.GetAsync($"/api/boards/by-project/{project.Id}");
        Assert.Equal(HttpStatusCode.OK, visibleBoards.StatusCode);
        var forbiddenBoardCreate = await _client.PostAsJsonAsync("/api/boards", new CreateBoardRequest(
            project.Id,
            "Viewer Board",
            "Kanban"));
        Assert.Equal(HttpStatusCode.Forbidden, forbiddenBoardCreate.StatusCode);
        var memberAudit = await _client.GetAsync($"/api/audit/entity/WorkItem/{auditedWorkItem.Id}");
        Assert.Equal(HttpStatusCode.OK, memberAudit.StatusCode);

        var outsider = await PostAsync<AuthResponse>("/api/auth/register", new RegisterUserRequest(
            "outsider",
            "outsider@zumbo.local",
            "P@ssword123",
            "org-2"));
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", outsider.AccessToken);
        var forbiddenReport = await _client.GetAsync($"/api/work-items/reports/project-summary/{project.Id}");
        Assert.Equal(HttpStatusCode.Forbidden, forbiddenReport.StatusCode);
        var forbiddenEntityAudit = await _client.GetAsync($"/api/audit/entity/WorkItem/{auditedWorkItem.Id}");
        Assert.Equal(HttpStatusCode.Forbidden, forbiddenEntityAudit.StatusCode);
        var forbiddenUserAudit = await _client.GetAsync($"/api/audit?actorUserId={owner.User.Id}");
        Assert.Equal(HttpStatusCode.Forbidden, forbiddenUserAudit.StatusCode);
    }

    [Fact]
    public async Task WorkItemSearch_UsesIndexAndSupportsArchiveRestoreLifecycle()
    {
        var stamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var register = await PostAsync<AuthResponse>("/api/auth/register", new RegisterUserRequest(
            "searcher" + stamp,
            "searcher" + stamp + "@zumbo.local",
            "P@ssword123",
            "org-search"));
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", register.AccessToken);

        var project = await PostAsync<ProjectResponse>("/api/projects", new CreateProjectRequest(
            "org-search",
            "SRC" + stamp.ToString()[^3..],
            "Search Project",
            register.User.Id));
        var board = await PostAsync<BoardResponse>("/api/boards", new CreateBoardRequest(project.Id, "Search Board", "Kanban"));

        var workItem = await PostAsync<WorkItemResponse>("/api/work-items", new CreateWorkItemRequest(
            project.Id,
            board.Id,
            "Indexed delivery task",
            "Task",
            "Medium",
            register.User.Id,
            null));

        var createdSearch = await GetAsync<IReadOnlyList<WorkItemResponse>>($"/api/work-items?projectId={project.Id}&text=indexed");
        Assert.Contains(createdSearch, x => x.Id == workItem.Id);

        var label = "search-label-" + stamp;
        workItem = await PostAsync<WorkItemResponse>($"/api/work-items/{workItem.Id}/labels", new AddLabelRequest(label));
        var labelSearch = await GetAsync<IReadOnlyList<WorkItemResponse>>($"/api/work-items?projectId={project.Id}&text={label}");
        Assert.Contains(labelSearch, x => x.Id == workItem.Id);

        workItem = await DeleteAsync<WorkItemResponse>($"/api/work-items/{workItem.Id}/labels/{label}");
        Assert.DoesNotContain(workItem.Labels, x => x == label);

        var updated = await PutAsync<WorkItemResponse>($"/api/work-items/{workItem.Id}", new UpdateWorkItemRequest(
            "Renamed searchable task",
            "Updated through index",
            "High",
            null));
        Assert.Equal("Renamed searchable task", updated.Title);

        var updatedSearch = await GetAsync<IReadOnlyList<WorkItemResponse>>($"/api/work-items?projectId={project.Id}&text=renamed");
        Assert.Contains(updatedSearch, x => x.Id == workItem.Id);
        var unscopedSearch = await _client.GetAsync("/api/work-items?text=renamed");
        Assert.Equal(HttpStatusCode.BadRequest, unscopedSearch.StatusCode);

        var deleteResponse = await _client.DeleteAsync($"/api/work-items/{workItem.Id}");
        deleteResponse.EnsureSuccessStatusCode();

        var archivedSearch = await GetAsync<IReadOnlyList<WorkItemResponse>>($"/api/work-items?projectId={project.Id}&text=renamed");
        Assert.DoesNotContain(archivedSearch, x => x.Id == workItem.Id);
        var archive = await GetAsync<IReadOnlyList<WorkItemResponse>>(
            $"/api/work-items?projectId={project.Id}&archived=true&text=index&page=1&pageSize=20");
        var archived = Assert.Single(archive, x => x.Id == workItem.Id);
        Assert.True(archived.Archived);
        Assert.Equal("Updated through index", archived.Description);

        var restored = await PostAsync<WorkItemResponse>($"/api/work-items/{workItem.Id}/restore", new { });
        Assert.False(restored.Archived);
        var restoredSearch = await GetAsync<IReadOnlyList<WorkItemResponse>>(
            $"/api/work-items?projectId={project.Id}&text=renamed");
        Assert.Contains(restoredSearch, x => x.Id == workItem.Id);
    }

    [Fact]
    public async Task InvalidLogin_ReturnsConsistentApiError()
    {
        var response = await _client.PostAsJsonAsync("/api/auth/login", new LoginRequest("missing", "bad-password"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<ApiResponse<object>>();
        Assert.False(body!.Success);
        Assert.Equal("UNAUTHORIZED", body.Error!.Code);
        Assert.False(string.IsNullOrWhiteSpace(body.CorrelationId));

        var forgot = await PostAsync<PasswordResetRequestedResponse>(
            "/api/auth/forgot-password",
            new ForgotPasswordRequest("missing@zumbo.local"));
        Assert.True(forgot.Accepted);
        var invalidReset = await _client.PostAsJsonAsync(
            "/api/auth/reset-password",
            new ResetPasswordRequest("invalid-token", "N3wP@ssword456"));
        Assert.Equal(HttpStatusCode.Unauthorized, invalidReset.StatusCode);
    }

    [Fact]
    public async Task InfrastructureEndpoints_ReportReadinessAndLimitSearchTraffic()
    {
        var live = await _client.GetAsync("/health/live");
        var ready = await _client.GetAsync("/health/ready");
        live.EnsureSuccessStatusCode();
        ready.EnsureSuccessStatusCode();

        var stamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var registration = await PostAsync<AuthResponse>("/api/auth/register", new RegisterUserRequest(
            "rate-user" + stamp,
            $"rate-user-{stamp}@zumbo.local",
            "P@ssword123",
            "org-rate-" + stamp));
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", registration.AccessToken);
        var project = await PostAsync<ProjectResponse>("/api/projects", new CreateProjectRequest(
            registration.User.OrganizationId,
            "RATE" + stamp.ToString()[^4..],
            "Rate Limit Project",
            registration.User.Id));

        HttpResponseMessage? response = null;
        for (var request = 0; request < 61; request++)
        {
            response?.Dispose();
            response = await _client.GetAsync($"/api/work-items?projectId={project.Id}&text=none");
        }

        Assert.Equal(HttpStatusCode.TooManyRequests, response!.StatusCode);
        var rateLimitError = await response.Content.ReadFromJsonAsync<ApiResponse<object>>();
        Assert.Equal("RATE_LIMIT_EXCEEDED", rateLimitError!.Error!.Code);
        response.Dispose();
    }

    [Fact]
    public async Task RealtimeHub_NegotiationRequiresAnActiveAuthenticatedSession()
    {
        _client.DefaultRequestHeaders.Authorization = null;
        var anonymous = await _client.PostAsync("/hubs/work-items/negotiate?negotiateVersion=1", null);
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);

        var stamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var registration = await PostAsync<AuthResponse>("/api/auth/register", new RegisterUserRequest(
            "realtime-user" + stamp,
            $"realtime-user-{stamp}@zumbo.local",
            "P@ssword123",
            "org-realtime-" + stamp));
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", registration.AccessToken);

        var authenticated = await _client.PostAsync("/hubs/work-items/negotiate?negotiateVersion=1", null);
        authenticated.EnsureSuccessStatusCode();
        var body = await authenticated.Content.ReadAsStringAsync();
        Assert.Contains("connectionToken", body, StringComparison.Ordinal);
        Assert.Contains("WebSockets", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task IdentityMfa_RequiresTotpConsumesRecoveryCodeAndInvalidatesSessions()
    {
        var stamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var username = "mfa-api-user" + stamp;
        var registration = await PostAsync<AuthResponse>("/api/auth/register", new RegisterUserRequest(
            username,
            $"mfa-api-user-{stamp}@zumbo.local",
            "P@ssword123",
            "org-mfa-api-" + stamp));
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", registration.AccessToken);

        var setup = await PostAsync<BeginMfaSetupResponse>(
            "/api/auth/mfa/setup",
            new BeginMfaSetupRequest("P@ssword123"));
        var code = TotpSecurity.GenerateCode(setup.Secret, DateTimeOffset.UtcNow);
        var confirmation = await PostAsync<ConfirmMfaSetupResponse>(
            "/api/auth/mfa/confirm",
            new ConfirmMfaSetupRequest(code));
        Assert.True(confirmation.Enabled);
        Assert.Equal(8, confirmation.RecoveryCodes.Count);

        var staleAccess = await _client.GetAsync("/api/auth/users");
        Assert.Equal(HttpStatusCode.Unauthorized, staleAccess.StatusCode);
        _client.DefaultRequestHeaders.Authorization = null;
        var missingMfa = await _client.PostAsJsonAsync(
            "/api/auth/login",
            new LoginRequest(username, "P@ssword123"));
        Assert.Equal(HttpStatusCode.Unauthorized, missingMfa.StatusCode);
        var missingMfaBody = await missingMfa.Content.ReadFromJsonAsync<ApiResponse<object>>();
        Assert.Equal("MFA_REQUIRED", missingMfaBody!.Error!.Code);

        var recoveryLogin = await PostAsync<AuthResponse>(
            "/api/auth/login",
            new LoginRequest(username, "P@ssword123", confirmation.RecoveryCodes.First()));
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", recoveryLogin.AccessToken);
        var status = await GetAsync<MfaStatusResponse>("/api/auth/mfa");
        Assert.True(status.Enabled);
        Assert.Equal(7, status.RemainingRecoveryCodes);

        var disabled = await PostAsync<MfaStatusResponse>(
            "/api/auth/mfa/disable",
            new DisableMfaRequest("P@ssword123", TotpSecurity.GenerateCode(setup.Secret, DateTimeOffset.UtcNow)));
        Assert.False(disabled.Enabled);
        _client.DefaultRequestHeaders.Authorization = null;
        var passwordOnly = await PostAsync<AuthResponse>(
            "/api/auth/login",
            new LoginRequest(username, "P@ssword123"));
        Assert.False(string.IsNullOrWhiteSpace(passwordOnly.AccessToken));
    }

    [Fact]
    public async Task IdentityApiKey_AuthenticatesTenantUserAndRevocationTakesEffectImmediately()
    {
        var stamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var registration = await PostAsync<AuthResponse>("/api/auth/register", new RegisterUserRequest(
            "api-key-user" + stamp,
            $"api-key-user-{stamp}@zumbo.local",
            "P@ssword123",
            "org-api-key-" + stamp));
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", registration.AccessToken);
        var created = await PostAsync<CreatedApiKeyResponse>(
            "/api/auth/api-keys",
            new CreateApiKeyRequest(
                "Automation client",
                "P@ssword123",
                null,
                DateTimeOffset.UtcNow.AddDays(30),
                ["api:full"]));
        Assert.StartsWith("zmb_", created.Key, StringComparison.Ordinal);

        _client.DefaultRequestHeaders.Authorization = null;
        _client.DefaultRequestHeaders.Add("X-API-Key", created.Key);
        var users = await GetAsync<IReadOnlyList<UserProfileResponse>>("/api/auth/users");
        Assert.Contains(users, x => x.Id == registration.User.Id);
        var listed = await GetAsync<IReadOnlyList<ApiKeyResponse>>("/api/auth/api-keys");
        Assert.Contains(listed, x => x.Id == created.Id && x.KeyPrefix == created.KeyPrefix);

        _client.DefaultRequestHeaders.Remove("X-API-Key");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", registration.AccessToken);
        var revoked = await _client.DeleteAsync($"/api/auth/api-keys/{created.Id}");
        revoked.EnsureSuccessStatusCode();

        _client.DefaultRequestHeaders.Authorization = null;
        _client.DefaultRequestHeaders.Add("X-API-Key", created.Key);
        var rejected = await _client.GetAsync("/api/auth/users");
        Assert.Equal(HttpStatusCode.Unauthorized, rejected.StatusCode);
        _client.DefaultRequestHeaders.Remove("X-API-Key");
    }

    [Fact]
    public async Task WorkItemBulkActions_AreBoundedAndInvalidateProjectSummary()
    {
        var stamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var organizationId = "org-bulk-" + stamp;
        var registration = await PostAsync<AuthResponse>("/api/auth/register", new RegisterUserRequest(
            "bulk-user" + stamp,
            $"bulk-user-{stamp}@zumbo.local",
            "P@ssword123",
            organizationId));
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", registration.AccessToken);
        var project = await PostAsync<ProjectResponse>("/api/projects", new CreateProjectRequest(
            organizationId,
            "BULK",
            "Bulk operations",
            registration.User.Id));
        var board = await PostAsync<BoardResponse>("/api/boards", new CreateBoardRequest(
            project.Id,
            "Bulk board",
            "Kanban"));
        var first = await PostAsync<WorkItemResponse>("/api/work-items", new CreateWorkItemRequest(
            project.Id, board.Id, "First bulk item", "Task", "Medium", registration.User.Id, null));
        var second = await PostAsync<WorkItemResponse>("/api/work-items", new CreateWorkItemRequest(
            project.Id, board.Id, "Second bulk item", "Task", "Medium", registration.User.Id, null));
        var ids = new[] { first.Id, second.Id };

        var initialSummary = await GetAsync<ProjectSummaryResponse>($"/api/work-items/reports/project-summary/{project.Id}");
        var moved = await PostAsync<BulkWorkItemResponse>(
            "/api/work-items/bulk/move",
            new BulkMoveWorkItemsRequest(ids, "In Progress"));
        var assigned = await PostAsync<BulkWorkItemResponse>(
            "/api/work-items/bulk/assign",
            new BulkAssignWorkItemsRequest(ids, registration.User.Id));
        var archived = await PostAsync<BulkWorkItemResponse>(
            "/api/work-items/bulk/archive",
            new BulkArchiveWorkItemsRequest(ids));
        var refreshedSummary = await GetAsync<ProjectSummaryResponse>($"/api/work-items/reports/project-summary/{project.Id}");

        Assert.Equal(2, initialSummary.Total);
        Assert.Equal(2, moved.Succeeded);
        Assert.Equal(2, assigned.Succeeded);
        Assert.Equal(2, archived.Succeeded);
        Assert.Equal(0, refreshedSummary.Total);

        var oversized = await _client.PostAsJsonAsync(
            "/api/work-items/bulk/archive",
            new BulkArchiveWorkItemsRequest(Enumerable.Range(0, 101).Select(index => "item-" + index).ToArray()));
        Assert.Equal(HttpStatusCode.BadRequest, oversized.StatusCode);
    }

    [Fact]
    public async Task IdentityPrivacy_ExportsOwnDataAndAnonymizationRevokesEveryCredential()
    {
        var stamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var username = "privacy-api-user" + stamp;
        var registration = await PostAsync<AuthResponse>("/api/auth/register", new RegisterUserRequest(
            username,
            $"privacy-api-user-{stamp}@zumbo.local",
            "P@ssword123",
            "org-privacy-api-" + stamp));
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", registration.AccessToken);
        var apiKey = await PostAsync<CreatedApiKeyResponse>(
            "/api/auth/api-keys",
            new CreateApiKeyRequest(
                "Privacy verification",
                "P@ssword123",
                null,
                DateTimeOffset.UtcNow.AddDays(30),
                ["api:full"]));

        var export = await GetAsync<PrivacyExportResponse>("/api/auth/privacy/export");
        Assert.Equal(registration.User.Id, export.Profile.Id);
        Assert.Contains(export.Data, x => x.Category == "audit");
        var wrongConfirmation = await _client.PostAsJsonAsync(
            "/api/auth/privacy/anonymize",
            new AnonymizeAccountRequest("P@ssword123", "DELETE"));
        Assert.Equal(HttpStatusCode.BadRequest, wrongConfirmation.StatusCode);

        var anonymized = await PostAsync<AnonymizeAccountResponse>(
            "/api/auth/privacy/anonymize",
            new AnonymizeAccountRequest("P@ssword123", "ANONYMIZE"));
        Assert.True(anonymized.Anonymized);
        Assert.StartsWith("anon-", anonymized.Pseudonym, StringComparison.Ordinal);

        var staleAccess = await _client.GetAsync("/api/auth/users");
        Assert.Equal(HttpStatusCode.Unauthorized, staleAccess.StatusCode);
        _client.DefaultRequestHeaders.Authorization = null;
        _client.DefaultRequestHeaders.Add("X-API-Key", apiKey.Key);
        var staleApiKey = await _client.GetAsync("/api/auth/users");
        Assert.Equal(HttpStatusCode.Unauthorized, staleApiKey.StatusCode);
        _client.DefaultRequestHeaders.Remove("X-API-Key");
        var oldPassword = await _client.PostAsJsonAsync(
            "/api/auth/login",
            new LoginRequest(username, "P@ssword123"));
        Assert.Equal(HttpStatusCode.Unauthorized, oldPassword.StatusCode);
    }

    [Fact]
    public async Task IdentityPrivacy_RequiresOrganizationOwnershipTransferBeforeAnonymization()
    {
        var stamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var organizationId = "org-privacy-owner-" + stamp;
        var owner = await PostAsync<AuthResponse>("/api/auth/register", new RegisterUserRequest(
            "privacy-owner" + stamp,
            $"privacy-owner-{stamp}@zumbo.local",
            "P@ssword123",
            organizationId));
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", owner.AccessToken);
        await PostAsync<OrganizationResponse>(
            "/api/organizations",
            new CreateOrganizationRequest("Privacy Ownership", organizationId));

        var blocked = await _client.PostAsJsonAsync(
            "/api/auth/privacy/anonymize",
            new AnonymizeAccountRequest("P@ssword123", "ANONYMIZE"));
        Assert.Equal(HttpStatusCode.Conflict, blocked.StatusCode);
        var body = await blocked.Content.ReadFromJsonAsync<ApiResponse<object>>();
        Assert.Equal("PRIVACY_OWNERSHIP_TRANSFER_REQUIRED", body!.Error!.Code);
        var stillActive = await _client.GetAsync("/api/auth/users");
        Assert.Equal(HttpStatusCode.OK, stillActive.StatusCode);
    }

    [Fact]
    public async Task IdentityLifecycle_InvalidatesSessionsOnPasswordLogoutAndDeactivation()
    {
        var stamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var username = "lifecycle" + stamp;
        var oldPassword = "P@ssword123";
        var newPassword = "N3wP@ssword456";
        var registration = await PostAsync<AuthResponse>("/api/auth/register", new RegisterUserRequest(
            username,
            $"{username}@zumbo.local",
            oldPassword,
            "org-lifecycle"));

        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", registration.AccessToken);
        var changed = await PostAsync<AuthResponse>("/api/auth/change-password", new ChangePasswordRequest(
            oldPassword,
            newPassword));

        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", registration.AccessToken);
        var oldAccessResponse = await _client.GetAsync("/api/auth/users");
        Assert.Equal(HttpStatusCode.Unauthorized, oldAccessResponse.StatusCode);

        var reusedRefresh = await _client.PostAsJsonAsync("/api/auth/refresh", new RefreshTokenRequest(registration.RefreshToken));
        Assert.Equal(HttpStatusCode.Unauthorized, reusedRefresh.StatusCode);

        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", changed.AccessToken);
        var compromisedSession = await _client.GetAsync("/api/auth/users");
        Assert.Equal(HttpStatusCode.Unauthorized, compromisedSession.StatusCode);

        var oldLogin = await _client.PostAsJsonAsync("/api/auth/login", new LoginRequest(username, oldPassword));
        Assert.Equal(HttpStatusCode.Unauthorized, oldLogin.StatusCode);
        var activeSession = await PostAsync<AuthResponse>("/api/auth/login", new LoginRequest(username, newPassword));

        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", activeSession.AccessToken);
        var logout = await PostAsync<LogoutResponse>("/api/auth/logout", new LogoutRequest(activeSession.RefreshToken));
        Assert.True(logout.LoggedOut);
        Assert.Equal(1, logout.RevokedSessions);
        var loggedOutAccess = await _client.GetAsync("/api/auth/users");
        Assert.Equal(HttpStatusCode.Unauthorized, loggedOutAccess.StatusCode);
        var loggedOutRefresh = await _client.PostAsJsonAsync("/api/auth/refresh", new RefreshTokenRequest(activeSession.RefreshToken));
        Assert.Equal(HttpStatusCode.Unauthorized, loggedOutRefresh.StatusCode);

        var finalSession = await PostAsync<AuthResponse>("/api/auth/login", new LoginRequest(username, newPassword));
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", finalSession.AccessToken);
        var deactivated = await PostAsync<AccountStatusResponse>("/api/auth/deactivate", new DeactivateAccountRequest(newPassword));
        Assert.False(deactivated.IsActive);

        var deactivatedAccess = await _client.GetAsync("/api/auth/users");
        Assert.Equal(HttpStatusCode.Unauthorized, deactivatedAccess.StatusCode);
        var deactivatedRefresh = await _client.PostAsJsonAsync("/api/auth/refresh", new RefreshTokenRequest(finalSession.RefreshToken));
        Assert.Equal(HttpStatusCode.Forbidden, deactivatedRefresh.StatusCode);
        var deactivatedLogin = await _client.PostAsJsonAsync("/api/auth/login", new LoginRequest(username, newPassword));
        Assert.Equal(HttpStatusCode.Forbidden, deactivatedLogin.StatusCode);
    }

    [Fact]
    public async Task IdentityRoleAdministration_EnforcesScopeAndInvalidatesExistingAccessTokens()
    {
        var stamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var organizationId = "org-role-api-" + stamp;
        var memberUsername = "role-api-member" + stamp;
        var member = await PostAsync<AuthResponse>("/api/auth/register", new RegisterUserRequest(
            memberUsername,
            $"role-api-member-{stamp}@zumbo.local",
            "P@ssword123",
            organizationId));

        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", member.AccessToken);
        var forbiddenCreate = await _client.PostAsJsonAsync("/api/auth/roles", new CreateRoleRequest(
            "Release Manager",
            organizationId,
            ["Release.Approve"]));
        Assert.Equal(HttpStatusCode.Forbidden, forbiddenCreate.StatusCode);

        var admin = await PostAsync<AuthResponse>("/api/auth/register", new RegisterUserRequest(
            "bootstrap-admin-" + stamp,
            "admin@zumbo.local",
            "P@ssword123",
            organizationId,
            "development-bootstrap-token"));
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", admin.AccessToken);
        var availableRoles = await GetAsync<IReadOnlyList<RoleResponse>>("/api/auth/roles");
        Assert.Contains(availableRoles, x => x.Name == "SystemAdmin" && x.IsSystem);
        var role = await PostAsync<RoleResponse>("/api/auth/roles", new CreateRoleRequest(
            "Release Manager",
            organizationId,
            ["Release.Approve", "Release.Publish", "AuditReadAll"]));
        var assigned = await PutAsync<UserProfileResponse>(
            $"/api/auth/users/{member.User.Id}/roles",
            new AssignUserRolesRequest([role.Name]));
        Assert.Contains(role.Name, assigned.Roles);

        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", member.AccessToken);
        var staleAccess = await _client.GetAsync("/api/auth/users");
        Assert.Equal(HttpStatusCode.Unauthorized, staleAccess.StatusCode);
        var refreshedMember = await PostAsync<AuthResponse>(
            "/api/auth/login",
            new LoginRequest(memberUsername, "P@ssword123"));
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", refreshedMember.AccessToken);
        var customPermissionAudit = await GetAsync<IReadOnlyList<AuditLogResponse>>(
            $"/api/audit/entity/Identity/{member.User.Id}");
        Assert.Contains(customPermissionAudit, x => x.Action == "UserRolesChanged");

        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", admin.AccessToken);
        var inUseDelete = await _client.DeleteAsync($"/api/auth/roles/{role.Id}");
        Assert.Equal(HttpStatusCode.Conflict, inUseDelete.StatusCode);
        await PutAsync<UserProfileResponse>(
            $"/api/auth/users/{member.User.Id}/roles",
            new AssignUserRolesRequest(["User"]));
        var deleted = await _client.DeleteAsync($"/api/auth/roles/{role.Id}");
        deleted.EnsureSuccessStatusCode();

        var identityAudit = await GetAsync<IReadOnlyList<AuditLogResponse>>(
            $"/api/audit/entity/Identity/{member.User.Id}");
        Assert.Contains(identityAudit, x => x.Action == "UserRolesChanged");
    }

    [Fact]
    public async Task TeamLifecycle_RequiresInviteRecipientAndTransfersOwnershipAtomically()
    {
        var stamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var organizationId = "org-team-" + stamp;
        var owner = await PostAsync<AuthResponse>("/api/auth/register", new RegisterUserRequest(
            "team-owner" + stamp,
            $"team-owner-{stamp}@zumbo.local",
            "P@ssword123",
            organizationId));
        var member = await PostAsync<AuthResponse>("/api/auth/register", new RegisterUserRequest(
            "team-member" + stamp,
            $"team-member-{stamp}@zumbo.local",
            "P@ssword123",
            organizationId));
        var wrongRecipient = await PostAsync<AuthResponse>("/api/auth/register", new RegisterUserRequest(
            "team-wrong" + stamp,
            $"team-wrong-{stamp}@zumbo.local",
            "P@ssword123",
            organizationId));

        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", owner.AccessToken);
        var team = await PostAsync<TeamResponse>("/api/teams", new CreateTeamRequest(
            organizationId,
            "Platform Team",
            owner.User.Id));
        team = await PostAsync<TeamResponse>($"/api/teams/{team.Id}/members", new InviteTeamMemberRequest(
            member.User.Email,
            "Member"));
        var inviteId = team.Members.Single(x => x.Email == member.User.Email).Id;

        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", wrongRecipient.AccessToken);
        var forbiddenAccept = await _client.PostAsync($"/api/teams/{team.Id}/invites/{inviteId}/accept", null);
        Assert.Equal(HttpStatusCode.Forbidden, forbiddenAccept.StatusCode);

        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", member.AccessToken);
        team = await PostWithoutBodyAsync<TeamResponse>($"/api/teams/{team.Id}/invites/{inviteId}/accept");
        Assert.Contains(team.Members, x => x.UserId == member.User.Id && x.Status == "Active");

        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", owner.AccessToken);
        team = await PatchAsync<TeamResponse>(
            $"/api/teams/{team.Id}/members/{member.User.Id}/role",
            new ChangeTeamMemberRoleRequest("Admin"));
        team = await PostAsync<TeamResponse>(
            $"/api/teams/{team.Id}/ownership-transfer",
            new TransferTeamOwnershipRequest(member.User.Id));
        Assert.Contains(team.Members, x => x.UserId == owner.User.Id && x.Role == "Admin");
        Assert.Contains(team.Members, x => x.UserId == member.User.Id && x.Role == "Owner");

        var formerOwnerArchive = await _client.DeleteAsync($"/api/teams/{team.Id}");
        Assert.Equal(HttpStatusCode.Forbidden, formerOwnerArchive.StatusCode);

        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", member.AccessToken);
        team = await PutAsync<TeamResponse>($"/api/teams/{team.Id}", new UpdateTeamRequest("Platform Core"));
        Assert.Equal("Platform Core", team.Name);
        var archive = await _client.DeleteAsync($"/api/teams/{team.Id}");
        archive.EnsureSuccessStatusCode();
        var teamAudit = await GetAsync<IReadOnlyList<AuditLogResponse>>(
            $"/api/audit/entity/Team/{team.Id}");
        Assert.Contains(teamAudit, x => x.Action == "TeamArchived");
        var visibleTeams = await GetAsync<IReadOnlyList<TeamResponse>>($"/api/teams?organizationId={organizationId}");
        Assert.DoesNotContain(visibleTeams, x => x.Id == team.Id);
        var archivedTeams = await GetAsync<IReadOnlyList<TeamResponse>>(
            $"/api/teams?organizationId={organizationId}&archived=true");
        Assert.Contains(archivedTeams, x => x.Id == team.Id && x.Archived);
        team = await PostAsync<TeamResponse>($"/api/teams/{team.Id}/restore", new { });
        Assert.False(team.Archived);
        teamAudit = await GetAsync<IReadOnlyList<AuditLogResponse>>($"/api/audit/entity/Team/{team.Id}");
        Assert.Contains(teamAudit, x => x.Action == "TeamRestored");
    }

    [Fact]
    public async Task ProjectLifecycle_WritesMemberRemovalAndArchiveAuditRecords()
    {
        var stamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var organizationId = "org-project-audit-" + stamp;
        var owner = await PostAsync<AuthResponse>("/api/auth/register", new RegisterUserRequest(
            "project-audit-owner" + stamp,
            $"project-audit-owner-{stamp}@zumbo.local",
            "P@ssword123",
            organizationId));
        var member = await PostAsync<AuthResponse>("/api/auth/register", new RegisterUserRequest(
            "project-audit-member" + stamp,
            $"project-audit-member-{stamp}@zumbo.local",
            "P@ssword123",
            organizationId));

        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", owner.AccessToken);
        var project = await PostAsync<ProjectResponse>("/api/projects", new CreateProjectRequest(
            organizationId,
            "PA" + stamp.ToString()[^4..],
            "Audited Project",
            owner.User.Id));
        project = await PostAsync<ProjectResponse>(
            $"/api/projects/{project.Id}/members",
            new AddProjectMemberRequest(member.User.Id, "Developer"));
        Assert.Contains(project.Members, x => x.UserId == member.User.Id);

        project = await DeleteAsync<ProjectResponse>($"/api/projects/{project.Id}/members/{member.User.Id}");
        Assert.DoesNotContain(project.Members, x => x.UserId == member.User.Id);
        var removalAudit = await GetAsync<IReadOnlyList<AuditLogResponse>>(
            $"/api/audit/entity/Project/{project.Id}");
        Assert.Contains(removalAudit, x => x.Action == "ProjectMemberRemoved");

        var archive = await _client.DeleteAsync($"/api/projects/{project.Id}");
        archive.EnsureSuccessStatusCode();
        var projectAudit = await GetAsync<IReadOnlyList<AuditLogResponse>>(
            $"/api/audit/entity/Project/{project.Id}");
        Assert.Contains(projectAudit, x => x.Action == "ProjectArchived");
        var archivedProjects = await GetAsync<IReadOnlyList<ProjectResponse>>(
            $"/api/projects?organizationId={organizationId}&archived=true");
        Assert.Contains(archivedProjects, x => x.Id == project.Id && x.Archived);
        project = await PostAsync<ProjectResponse>($"/api/projects/{project.Id}/restore", new { });
        Assert.False(project.Archived);
        projectAudit = await GetAsync<IReadOnlyList<AuditLogResponse>>($"/api/audit/entity/Project/{project.Id}");
        Assert.Contains(projectAudit, x => x.Action == "ProjectRestored");
    }

    [Fact]
    public async Task BoardLifecycle_EnforcesColumnWipLimitAndUsageRules()
    {
        var stamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var organizationId = "org-board-" + stamp;
        var owner = await PostAsync<AuthResponse>("/api/auth/register", new RegisterUserRequest(
            "board-owner" + stamp,
            $"board-owner-{stamp}@zumbo.local",
            "P@ssword123",
            organizationId));
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", owner.AccessToken);
        var project = await PostAsync<ProjectResponse>("/api/projects", new CreateProjectRequest(
            organizationId,
            "WIP" + stamp.ToString()[^3..],
            "WIP Project",
            owner.User.Id));
        var board = await PostAsync<BoardResponse>("/api/boards", new CreateBoardRequest(
            project.Id,
            "Flow Board",
            "Kanban"));
        var lifecycleBoard = await PostAsync<BoardResponse>("/api/boards", new CreateBoardRequest(
            project.Id,
            "Lifecycle Board",
            "Kanban"));
        var archiveBoard = await _client.DeleteAsync($"/api/boards/{lifecycleBoard.Id}");
        archiveBoard.EnsureSuccessStatusCode();
        var archivedBoards = await GetAsync<IReadOnlyList<BoardResponse>>(
            $"/api/boards/by-project/{project.Id}?archived=true");
        Assert.Contains(archivedBoards, x => x.Id == lifecycleBoard.Id && x.Archived);
        lifecycleBoard = await PostAsync<BoardResponse>($"/api/boards/{lifecycleBoard.Id}/restore", new { });
        Assert.False(lifecycleBoard.Archived);
        var inProgress = board.Columns.Single(x => x.Category == "InProgress");
        board = await PutAsync<BoardResponse>(
            $"/api/boards/{board.Id}/columns/{inProgress.Id}",
            new UpdateColumnRequest(inProgress.Name, inProgress.Category, 1));
        inProgress = board.Columns.Single(x => x.Id == inProgress.Id);
        Assert.Equal(1, inProgress.WipLimit);

        var first = await PostAsync<WorkItemResponse>("/api/work-items", new CreateWorkItemRequest(
            project.Id, board.Id, "First active item", "Task", "Medium", owner.User.Id, null));
        var second = await PostAsync<WorkItemResponse>("/api/work-items", new CreateWorkItemRequest(
            project.Id, board.Id, "Second active item", "Task", "Medium", owner.User.Id, null));
        first = await PatchAsync<WorkItemResponse>(
            $"/api/work-items/{first.Id}/status",
            new MoveWorkItemRequest("In Progress"));
        Assert.Equal(inProgress.Id, first.ColumnId);

        var wipExceeded = await _client.PatchAsJsonAsync(
            $"/api/work-items/{second.Id}/status",
            new MoveWorkItemRequest("In Progress"));
        Assert.Equal(HttpStatusCode.Conflict, wipExceeded.StatusCode);
        var wipBody = await wipExceeded.Content.ReadFromJsonAsync<ApiResponse<object>>();
        Assert.Equal("BOARD_WIP_LIMIT_EXCEEDED", wipBody!.Error!.Code);

        first = await PatchAsync<WorkItemResponse>(
            $"/api/work-items/{first.Id}/status",
            new MoveWorkItemRequest("Code Review"));
        var third = await PostAsync<WorkItemResponse>("/api/work-items", new CreateWorkItemRequest(
            project.Id, board.Id, "Concurrent active item", "Task", "Medium", owner.User.Id, null));
        var concurrentMoves = await Task.WhenAll(
            _client.PatchAsJsonAsync($"/api/work-items/{second.Id}/status", new MoveWorkItemRequest("In Progress")),
            _client.PatchAsJsonAsync($"/api/work-items/{third.Id}/status", new MoveWorkItemRequest("In Progress")));
        Assert.Single(concurrentMoves, x => x.StatusCode == HttpStatusCode.OK);
        Assert.Single(concurrentMoves, x => x.StatusCode == HttpStatusCode.Conflict);

        var renameInUse = await _client.PutAsJsonAsync(
            $"/api/boards/{board.Id}/columns/{inProgress.Id}",
            new UpdateColumnRequest("Doing", inProgress.Category, 1));
        Assert.Equal(HttpStatusCode.Conflict, renameInUse.StatusCode);
        var deleteInUse = await _client.DeleteAsync($"/api/boards/{board.Id}/columns/{inProgress.Id}");
        Assert.Equal(HttpStatusCode.Conflict, deleteInUse.StatusCode);
        var archiveInUse = await _client.DeleteAsync($"/api/boards/{board.Id}");
        Assert.Equal(HttpStatusCode.Conflict, archiveInUse.StatusCode);
    }

    [Fact]
    public async Task WorkItemHierarchyAndDependencies_AreEnforcedThroughHttpApi()
    {
        var stamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var organizationId = "org-structure-" + stamp;
        var owner = await PostAsync<AuthResponse>("/api/auth/register", new RegisterUserRequest(
            "structure-owner" + stamp,
            $"structure-owner-{stamp}@zumbo.local",
            "P@ssword123",
            organizationId));
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", owner.AccessToken);
        var project = await PostAsync<ProjectResponse>("/api/projects", new CreateProjectRequest(
            organizationId,
            "STR" + stamp.ToString()[^3..],
            "Structure Project",
            owner.User.Id));
        var board = await PostAsync<BoardResponse>("/api/boards", new CreateBoardRequest(
            project.Id,
            "Structure Board",
            "Kanban"));

        var epic = await PostAsync<WorkItemResponse>("/api/work-items", new CreateWorkItemRequest(
            project.Id, board.Id, "Delivery epic", "Epic", "High", owner.User.Id, null));
        var story = await PostAsync<WorkItemResponse>("/api/work-items", new CreateWorkItemRequest(
            project.Id, board.Id, "Delivery story", "Story", "High", owner.User.Id, null, epic.Id));
        var subtask = await PostAsync<WorkItemResponse>("/api/work-items", new CreateWorkItemRequest(
            project.Id, board.Id, "Delivery subtask", "Subtask", "Medium", owner.User.Id, null, story.Id));
        Assert.Equal(epic.Id, story.ParentId);
        Assert.Equal(story.Id, subtask.ParentId);

        var blocker = await PostAsync<WorkItemResponse>("/api/work-items", new CreateWorkItemRequest(
            project.Id, board.Id, "Open blocker", "Bug", "High", owner.User.Id, null));
        story = await PostAsync<WorkItemResponse>(
            $"/api/work-items/{story.Id}/relations",
            new LinkWorkItemRequest(blocker.Id, "BlockedBy"));
        Assert.Contains(story.Relations, x => x.RelatedWorkItemId == blocker.Id && x.RelationType == "BlockedBy");

        story = await PatchAsync<WorkItemResponse>($"/api/work-items/{story.Id}/status", new MoveWorkItemRequest("In Progress"));
        story = await PatchAsync<WorkItemResponse>($"/api/work-items/{story.Id}/status", new MoveWorkItemRequest("Code Review"));
        story = await PatchAsync<WorkItemResponse>($"/api/work-items/{story.Id}/status", new MoveWorkItemRequest("Test"));
        var blockedCompletion = await _client.PatchAsJsonAsync(
            $"/api/work-items/{story.Id}/status",
            new MoveWorkItemRequest("Done"));
        Assert.Equal(HttpStatusCode.Conflict, blockedCompletion.StatusCode);

        story = await DeleteAsync<WorkItemResponse>(
            $"/api/work-items/{story.Id}/relations/{blocker.Id}?relationType=BlockedBy");
        Assert.Empty(story.Relations);
        var activeChildCompletion = await _client.PatchAsJsonAsync(
            $"/api/work-items/{story.Id}/status",
            new MoveWorkItemRequest("Done"));
        Assert.Equal(HttpStatusCode.Conflict, activeChildCompletion.StatusCode);
        var body = await activeChildCompletion.Content.ReadFromJsonAsync<ApiResponse<object>>();
        Assert.Equal("WORK_ITEM_HAS_ACTIVE_CHILDREN", body!.Error!.Code);
    }

    [Fact]
    public async Task OrganizationEndpoints_EnforceOwnerAndTenantIsolation()
    {
        var stamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var tenantKey = "tenant-" + stamp;
        var owner = await PostAsync<AuthResponse>("/api/auth/register", new RegisterUserRequest(
            "organization-owner" + stamp,
            $"organization-owner-{stamp}@zumbo.local",
            "P@ssword123",
            tenantKey));
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", owner.AccessToken);
        var organization = await PostAsync<OrganizationResponse>("/api/organizations", new CreateOrganizationRequest(
            "Organization " + stamp,
            tenantKey));
        organization = await PostAsync<OrganizationResponse>(
            $"/api/organizations/{organization.Id}/departments",
            new CreateDepartmentRequest("Engineering", null));
        Assert.Equal(tenantKey, organization.Id);
        Assert.Equal(owner.User.Id, organization.OwnerUserId);
        Assert.Single(organization.Departments);

        var tenantMember = await PostAsync<AuthResponse>("/api/auth/register", new RegisterUserRequest(
            "organization-member" + stamp,
            $"organization-member-{stamp}@zumbo.local",
            "P@ssword123",
            tenantKey));
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tenantMember.AccessToken);
        var visibleOrganizations = await GetAsync<IReadOnlyList<OrganizationResponse>>("/api/organizations");
        Assert.Single(visibleOrganizations);
        var forbiddenUpdate = await _client.PutAsJsonAsync(
            $"/api/organizations/{organization.Id}",
            new UpdateOrganizationRequest("Unauthorized"));
        Assert.Equal(HttpStatusCode.Forbidden, forbiddenUpdate.StatusCode);

        var outsider = await PostAsync<AuthResponse>("/api/auth/register", new RegisterUserRequest(
            "organization-outsider" + stamp,
            $"organization-outsider-{stamp}@zumbo.local",
            "P@ssword123",
            "other-" + stamp));
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", outsider.AccessToken);
        var hiddenOrganizations = await GetAsync<IReadOnlyList<OrganizationResponse>>("/api/organizations");
        Assert.Empty(hiddenOrganizations);
    }

    [Fact]
    public async Task WorkflowApprovalAndAutomation_EnforceProjectRolesEndToEnd()
    {
        var stamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var organizationId = "org-workflow-" + stamp;
        var owner = await PostAsync<AuthResponse>("/api/auth/register", new RegisterUserRequest(
            "workflow-owner" + stamp,
            $"workflow-owner-{stamp}@zumbo.local",
            "P@ssword123",
            organizationId));
        var developer = await PostAsync<AuthResponse>("/api/auth/register", new RegisterUserRequest(
            "workflow-developer" + stamp,
            $"workflow-developer-{stamp}@zumbo.local",
            "P@ssword123",
            organizationId));
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", owner.AccessToken);
        var team = await PostAsync<TeamResponse>("/api/teams", new CreateTeamRequest(
            organizationId,
            "Workflow Team " + stamp,
            owner.User.Id));
        team = await PostAsync<TeamResponse>(
            $"/api/teams/{team.Id}/members",
            new InviteTeamMemberRequest(developer.User.Email, "Member"));
        var inviteId = team.Members.Single(x => x.Email == developer.User.Email && x.Status == "Invited").Id;
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", developer.AccessToken);
        team = await PostWithoutBodyAsync<TeamResponse>($"/api/teams/{team.Id}/invites/{inviteId}/accept");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", owner.AccessToken);
        var project = await PostAsync<ProjectResponse>("/api/projects", new CreateProjectRequest(
            organizationId,
            "WF" + stamp.ToString()[^4..],
            "Workflow Project",
            owner.User.Id));
        project = await PostAsync<ProjectResponse>(
            $"/api/projects/{project.Id}/members",
            new AddProjectMemberRequest(developer.User.Id, "Developer"));
        project = await PostAsync<ProjectResponse>(
            $"/api/projects/{project.Id}/teams",
            new AddProjectTeamRequest(team.Id));
        Assert.Contains(team.Id, project.TeamIds);
        var board = await PostAsync<BoardResponse>("/api/boards", new CreateBoardRequest(
            project.Id,
            "Approval Board",
            "Kanban"));
        board = await PatchAsync<BoardResponse>(
            $"/api/boards/{board.Id}/swimlane",
            new UpdateSwimlaneRequest("Team"));
        board = await PostAsync<BoardResponse>(
            $"/api/boards/{board.Id}/views",
            new CreateBoardViewRequest(
                "Owner private",
                false,
                "Assignee",
                new BoardFilterRequest(owner.User.Id, null, [], [], [], null)));
        board = await PostAsync<BoardResponse>(
            $"/api/boards/{board.Id}/views",
            new CreateBoardViewRequest(
                "Shared team queue",
                true,
                "Team",
                new BoardFilterRequest(null, team.Id, ["To Do", "In Progress"], [], [], null)));
        Assert.Equal("Team", board.SwimlaneMode);
        var statuses = new[]
        {
            new WorkflowStatusRequest("To Do", "Todo"),
            new WorkflowStatusRequest("In Progress", "InProgress"),
            new WorkflowStatusRequest("Code Review", "InProgress"),
            new WorkflowStatusRequest("Test", "InProgress"),
            new WorkflowStatusRequest("Done", "Done")
        };
        var transitions = new[]
        {
            new WorkflowTransitionRequest("To Do", "In Progress", false, false),
            new WorkflowTransitionRequest("In Progress", "Code Review", true, false),
            new WorkflowTransitionRequest("Code Review", "Test", true, false),
            new WorkflowTransitionRequest(
                "Test",
                "Done",
                false,
                true,
                true,
                [new WorkflowAutomationRequest("AddLabel", "approved-release")])
        };
        var workflow = await PutAsync<WorkflowResponse>(
            $"/api/workflows/{project.Id}",
            new CreateWorkflowRequest("ignored-route-body", transitions, statuses));
        Assert.Equal(project.Id, workflow.ProjectId);

        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", developer.AccessToken);
        var developerBoard = (await GetAsync<IReadOnlyList<BoardResponse>>(
            $"/api/boards/by-project/{project.Id}")).Single(x => x.Id == board.Id);
        Assert.Single(developerBoard.Views);
        Assert.True(developerBoard.Views.Single().IsShared);
        developerBoard = await PostAsync<BoardResponse>(
            $"/api/boards/{board.Id}/views",
            new CreateBoardViewRequest(
                "My work",
                false,
                "Assignee",
                new BoardFilterRequest(developer.User.Id, team.Id, [], ["High"], [], null)));
        Assert.Equal(2, developerBoard.Views.Count);
        var item = await PostAsync<WorkItemResponse>("/api/work-items", new CreateWorkItemRequest(
            project.Id, board.Id, "Approval delivery", "Task", "High", developer.User.Id, null, null, team.Id));
        item = await PatchAsync<WorkItemResponse>($"/api/work-items/{item.Id}/status", new MoveWorkItemRequest("In Progress"));
        item = await PatchAsync<WorkItemResponse>($"/api/work-items/{item.Id}/status", new MoveWorkItemRequest("Code Review"));
        item = await PatchAsync<WorkItemResponse>($"/api/work-items/{item.Id}/status", new MoveWorkItemRequest("Test"));
        item = await PostAsync<WorkItemResponse>(
            $"/api/work-items/{item.Id}/approvals",
            new RequestWorkItemApprovalRequest("Done"));
        var approvalId = item.Approvals.Single().Id;
        var preference = await PutAsync<NotificationPreferenceResponse>(
            "/api/notifications/preferences/me",
            new UpdateNotificationPreferencesRequest(true, false, ["Mention"]));
        Assert.False(preference.EmailEnabled);
        var developerNotifications = await GetAsync<IReadOnlyList<NotificationResponse>>("/api/notifications?page=1&pageSize=10");
        var developerNotificationId = developerNotifications.First().Id;
        var developerDecision = await _client.PostAsJsonAsync(
            $"/api/work-items/{item.Id}/approvals/{approvalId}/decision",
            new DecideWorkItemApprovalRequest(true, "Self approval"));
        Assert.Equal(HttpStatusCode.Forbidden, developerDecision.StatusCode);

        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", owner.AccessToken);
        var forbiddenNotifications = await _client.GetAsync($"/api/notifications/{developer.User.Id}");
        Assert.Equal(HttpStatusCode.Forbidden, forbiddenNotifications.StatusCode);
        var hiddenNotificationRead = await _client.PatchAsJsonAsync(
            $"/api/notifications/{developerNotificationId}/read",
            new { });
        Assert.Equal(HttpStatusCode.NotFound, hiddenNotificationRead.StatusCode);
        item = await PostAsync<WorkItemResponse>(
            $"/api/work-items/{item.Id}/approvals/{approvalId}/decision",
            new DecideWorkItemApprovalRequest(true, "Owner approved"));
        Assert.Equal("Approved", item.Approvals.Single().Status);

        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", developer.AccessToken);
        item = await PatchAsync<WorkItemResponse>($"/api/work-items/{item.Id}/status", new MoveWorkItemRequest("Done"));
        Assert.Equal("Done", item.Status);
        Assert.NotNull(item.Approvals.Single().ConsumedAt);
        Assert.Contains("approved-release", item.Labels);
        var reportDate = DateOnly.FromDateTime(DateTime.UtcNow).ToString("yyyy-MM-dd");
        var completionRate = await GetAsync<TaskCompletionRateResponse>(
            $"/api/work-items/reports/completion-rate/{project.Id}?from={reportDate}&to={reportDate}");
        var teamPerformance = await GetAsync<IReadOnlyList<TeamPerformanceResponse>>(
            $"/api/work-items/reports/team-performance/{project.Id}?from={reportDate}&to={reportDate}");
        Assert.Equal(100, completionRate.CompletionRatePercent);
        Assert.Contains(teamPerformance, x => x.TeamId == team.Id && x.CompletedItems == 1);
    }

    private async Task<T> GetAsync<T>(string url)
    {
        var response = await _client.GetAsync(url);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<ApiResponse<T>>();
        Assert.NotNull(body);
        Assert.True(body!.Success);
        return body.Data!;
    }

    private async Task<T> PostAsync<T>(string url, object request)
    {
        var response = await _client.PostAsJsonAsync(url, request);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<ApiResponse<T>>();
        Assert.NotNull(body);
        Assert.True(body!.Success);
        return body.Data!;
    }

    private async Task<T> PostWithoutBodyAsync<T>(string url)
    {
        var response = await _client.PostAsync(url, null);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<ApiResponse<T>>();
        Assert.NotNull(body);
        Assert.True(body!.Success);
        return body.Data!;
    }

    private async Task<T> PatchAsync<T>(string url, object request)
    {
        var response = await _client.PatchAsJsonAsync(url, request);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<ApiResponse<T>>();
        Assert.NotNull(body);
        Assert.True(body!.Success);
        return body.Data!;
    }

    private async Task<T> PutAsync<T>(string url, object request)
    {
        var response = await _client.PutAsJsonAsync(url, request);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<ApiResponse<T>>();
        Assert.NotNull(body);
        Assert.True(body!.Success);
        return body.Data!;
    }

    private async Task<T> DeleteAsync<T>(string url)
    {
        var response = await _client.DeleteAsync(url);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<ApiResponse<T>>();
        Assert.NotNull(body);
        Assert.True(body!.Success);
        return body.Data!;
    }
}
