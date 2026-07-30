using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
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

public sealed class IntakeApiTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> factory;

    public IntakeApiTests(WebApplicationFactory<Program> factory)
    {
        this.factory = factory;
    }

    [Fact]
    public async Task FormSubmissionAttachmentRoutingTriageAndAudit_AreTenantSafeAndIdempotent()
    {
        using var client = CreateClient(20);
        var stamp = Guid.NewGuid().ToString("N");
        var tenant = "intake-" + stamp;
        var owner = await RegisterAsync(client, "intake-owner-" + stamp, tenant);
        var outsider = await RegisterAsync(client, "intake-outsider-" + stamp, "foreign-" + stamp);
        Authorize(client, owner);
        await PostAsync<OrganizationResponse>(
            client,
            "/api/organizations",
            new CreateOrganizationRequest("Intake organization", tenant));
        var project = await PostAsync<ProjectResponse>(
            client,
            "/api/projects",
            new CreateProjectRequest(tenant, "I" + stamp[..7], "Intake project", owner.User.Id));
        var board = await PostAsync<BoardResponse>(
            client,
            "/api/boards",
            new CreateBoardRequest(project.Id, "Intake board", "Kanban"));
        var form = await PostAsync<IntakeFormResponse>(
            client,
            "/api/intake/forms",
            new CreateIntakeFormRequest(
                project.Id,
                "IT service request",
                "Submit a request for service desk triage.",
                Definition(board.Id, IntakeAccessPolicies.Public, attachmentRequired: true)));
        form = await PostAsync<IntakeFormResponse>(
            client,
            $"/api/intake/forms/{form.Id}/publish",
            new { });
        Assert.Equal(IntakeFormStates.Published, form.State);
        Assert.NotNull(form.PublicId);

        client.DefaultRequestHeaders.Authorization = null;
        var published = await GetAsync<PublishedIntakeFormResponse>(
            client,
            $"/api/intake/public/forms/{form.PublicId}");
        Assert.Equal(1, published.Version);
        Assert.Equal(IntakeAccessPolicies.Public, published.AccessPolicy);

        var request = new CreateIntakeSubmissionRequest(
        [
            new("summary", "VPN access fails"),
            new("details", "The client reports an authentication loop."),
            new("severity", "High"),
            new("needed_by", "2026-08-03")
        ]);
        var first = await SubmitMultipartAsync(
            client,
            form.PublicId!,
            request,
            "intake-key-1",
            includeAttachment: true);
        var duplicate = await SubmitMultipartAsync(
            client,
            form.PublicId!,
            request,
            "intake-key-1",
            includeAttachment: true);
        Assert.Equal(first, duplicate);
        Assert.Equal(IntakeSubmissionStates.New, first.State);

        var changed = await SendMultipartAsync(
            client,
            form.PublicId!,
            request with
            {
                Values =
                [
                    new("summary", "Changed summary"),
                    new("severity", "High")
                ]
            },
            "intake-key-1",
            includeAttachment: true);
        await AssertErrorAsync(changed, HttpStatusCode.Conflict, "IDEMPOTENCY_KEY_REUSED");

        Authorize(client, owner);
        var queue = await GetAsync<IntakeSubmissionPage>(
            client,
            $"/api/intake/forms/{form.Id}/submissions?state=New&page=1&pageSize=20");
        Assert.Single(queue.Items);
        Assert.Null(first.WorkItemId);
        var queuedSubmission = queue.Items.Single();
        var workItem = await GetAsync<WorkItemResponse>(
            client,
            $"/api/work-items/{queuedSubmission.WorkItemId}");
        Assert.Equal("VPN access fails", workItem.Title);
        Assert.Equal("The client reports an authentication loop.", workItem.Description);
        Assert.Equal("High", workItem.Priority);
        Assert.Single(workItem.Attachments);
        Assert.Equal(AttachmentSecurityStates.Clean, workItem.Attachments.Single().SecurityState);

        var triaged = await PostAsync<IntakeSubmissionResponse>(
            client,
            $"/api/intake/forms/{form.Id}/submissions/{first.SubmissionId}/triage",
            new TriageIntakeSubmissionRequest(
                IntakeSubmissionStates.InReview,
                "Assigned to access operations."));
        Assert.Equal(IntakeSubmissionStates.InReview, triaged.State);
        Assert.Equal(owner.User.Id, triaged.TriagedByUserId);

        var audit = await AwaitAuditAsync(
            client,
            first.SubmissionId,
            "IntakeSubmissionTriaged");
        Assert.Contains(audit, x => x.Action == "IntakeSubmissionReceived");
        Assert.Contains(audit, x => x.Action == "IntakeSubmissionRouted");
        Assert.Contains(audit, x => x.Action == "IntakeSubmissionTriaged");

        Authorize(client, outsider);
        await AssertErrorAsync(
            await client.GetAsync($"/api/intake/forms/{form.Id}/submissions"),
            HttpStatusCode.NotFound,
            "PROJECT_NOT_FOUND");
    }

    [Fact]
    public async Task PublicSubmissionRateLimit_IsFailClosedPerClient()
    {
        using var client = CreateClient(5);
        var stamp = Guid.NewGuid().ToString("N");
        var tenant = "intake-rate-" + stamp;
        var owner = await RegisterAsync(client, "intake-rate-owner-" + stamp, tenant);
        Authorize(client, owner);
        await PostAsync<OrganizationResponse>(
            client,
            "/api/organizations",
            new CreateOrganizationRequest("Intake rate organization", tenant));
        var project = await PostAsync<ProjectResponse>(
            client,
            "/api/projects",
            new CreateProjectRequest(tenant, "R" + stamp[..7], "Rate project", owner.User.Id));
        var board = await PostAsync<BoardResponse>(
            client,
            "/api/boards",
            new CreateBoardRequest(project.Id, "Rate board", "Kanban"));
        var form = await PostAsync<IntakeFormResponse>(
            client,
            "/api/intake/forms",
            new CreateIntakeFormRequest(
                project.Id,
                "Public request",
                null,
                Definition(board.Id, IntakeAccessPolicies.Public, attachmentRequired: false)));
        form = await PostAsync<IntakeFormResponse>(
            client,
            $"/api/intake/forms/{form.Id}/publish",
            new { });
        client.DefaultRequestHeaders.Authorization = null;

        var request = new CreateIntakeSubmissionRequest(
        [
            new("summary", "Rate limited request"),
            new("severity", "Low")
        ]);
        for (var index = 1; index <= 5; index++)
        {
            _ = await SubmitJsonAsync(
                client,
                form.PublicId!,
                request,
                "rate-key-" + index);
        }
        using var rejectedRequest = new HttpRequestMessage(
            HttpMethod.Post,
            $"/api/intake/public/forms/{form.PublicId}/submissions")
        {
            Content = JsonContent.Create(request)
        };
        rejectedRequest.Headers.TryAddWithoutValidation("Idempotency-Key", "rate-key-6");
        var rejected = await client.SendAsync(rejectedRequest);

        await AssertErrorAsync(
            rejected,
            HttpStatusCode.TooManyRequests,
            "RATE_LIMIT_EXCEEDED");
    }

    private HttpClient CreateClient(int publicPermitLimit) =>
        factory.WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["RateLimiting:IntakePublicPermitLimit"] = publicPermitLimit.ToString(),
                    ["BackgroundJobs:Enabled"] = "true"
                })))
            .CreateClient();

    private static IntakeFormDefinitionRequest Definition(
        string boardId,
        string accessPolicy,
        bool attachmentRequired) => new(
        accessPolicy,
        boardId,
        "Task",
        "Medium",
        "Your request is ready for triage.",
        [
            new("summary", "Summary", IntakeFieldTypes.Text, Required: true),
            new("details", "Details", IntakeFieldTypes.LongText),
            new("severity", "Severity", IntakeFieldTypes.Choice, Options: ["Low", "High"]),
            new("needed_by", "Needed by", IntakeFieldTypes.Date),
            new("evidence", "Evidence", IntakeFieldTypes.Attachment, Required: attachmentRequired)
        ],
        new IntakeFieldMappingRequest(
            "summary",
            "details",
            "severity",
            "needed_by"));

    private static async Task<IntakeSubmissionConfirmationResponse> SubmitMultipartAsync(
        HttpClient client,
        string publicId,
        CreateIntakeSubmissionRequest request,
        string key,
        bool includeAttachment)
    {
        var response = await SendMultipartAsync(client, publicId, request, key, includeAttachment);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<
            ApiResponse<IntakeSubmissionConfirmationResponse>>())!.Data!;
    }

    private static async Task<HttpResponseMessage> SendMultipartAsync(
        HttpClient client,
        string publicId,
        CreateIntakeSubmissionRequest request,
        string key,
        bool includeAttachment)
    {
        var content = new MultipartFormDataContent();
        content.Add(
            new StringContent(
                JsonSerializer.Serialize(request),
                Encoding.UTF8,
                "application/json"),
            "submission");
        if (includeAttachment)
        {
            var file = new ByteArrayContent(Encoding.UTF8.GetBytes("vpn trace"));
            file.Headers.ContentType = new MediaTypeHeaderValue("text/plain");
            content.Add(file, "attachments.evidence", "trace.txt");
        }
        var message = new HttpRequestMessage(
            HttpMethod.Post,
            $"/api/intake/public/forms/{publicId}/submissions")
        {
            Content = content
        };
        message.Headers.TryAddWithoutValidation("Idempotency-Key", key);
        return await client.SendAsync(message);
    }

    private static async Task<IntakeSubmissionConfirmationResponse> SubmitJsonAsync(
        HttpClient client,
        string publicId,
        CreateIntakeSubmissionRequest request,
        string key)
    {
        using var message = new HttpRequestMessage(
            HttpMethod.Post,
            $"/api/intake/public/forms/{publicId}/submissions")
        {
            Content = JsonContent.Create(request)
        };
        message.Headers.TryAddWithoutValidation("Idempotency-Key", key);
        var response = await client.SendAsync(message);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<
            ApiResponse<IntakeSubmissionConfirmationResponse>>())!.Data!;
    }

    private static async Task<IReadOnlyCollection<AuditLogResponse>> AwaitAuditAsync(
        HttpClient client,
        string submissionId,
        string expectedAction)
    {
        for (var attempt = 0; attempt < 100; attempt++)
        {
            var audit = await GetAsync<IReadOnlyCollection<AuditLogResponse>>(
                client,
                $"/api/audit/entity/IntakeSubmission/{submissionId}");
            if (audit.Any(x => x.Action == expectedAction))
            {
                return audit;
            }
            await Task.Delay(50);
        }
        throw new Xunit.Sdk.XunitException(
            $"Intake audit action {expectedAction} was not visible within the bounded wait.");
    }

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
        var error = await response.Content.ReadFromJsonAsync<ApiResponse<JsonElement>>();
        Assert.Equal(code, error!.Error!.Code);
    }
}
