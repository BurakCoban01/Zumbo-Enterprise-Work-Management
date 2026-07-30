using System.Text.Json;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.Modules.WorkItems;
using Zumbo.SharedKernel;

using static ApiEndpointResults;

internal static class IntakeEndpoints
{
    internal static void MapIntakeEndpoints(this RouteGroupBuilder api)
    {
        var forms = api.MapGroup("/intake/forms")
            .WithTags("Intake")
            .RequireAuthorization()
            .WithZumboPermission(PermissionCatalog.WorkItemView);
        forms.AddEndpointFilter<WorkItemTransactionFilter>();

        forms.MapPost("/", async (
                CreateIntakeFormRequest request,
                IntakeFormService service,
                HttpContext http,
                CancellationToken ct) =>
            Created(await service.CreateAsync(request, CorrelationId(http), ct), http))
            .WithZumboPermission(PermissionCatalog.WorkflowManage);

        forms.MapGet("/", async (
                string projectId,
                IntakeFormService service,
                HttpContext http,
                CancellationToken ct) =>
            Ok(await service.ListAsync(projectId, ct), http));

        forms.MapGet("/{formId}", async (
                string formId,
                IntakeFormService service,
                HttpContext http,
                CancellationToken ct) =>
            Ok(await service.GetAsync(formId, ct), http));

        forms.MapPut("/{formId}", async (
                string formId,
                UpdateIntakeFormRequest request,
                IntakeFormService service,
                HttpContext http,
                CancellationToken ct) =>
            Ok(await service.UpdateAsync(formId, request, CorrelationId(http), ct), http))
            .WithZumboPermission(PermissionCatalog.WorkflowManage);

        forms.MapPost("/{formId}/publish", async (
                string formId,
                IntakeFormService service,
                HttpContext http,
                CancellationToken ct) =>
            Ok(await service.PublishAsync(formId, CorrelationId(http), ct), http))
            .WithZumboPermission(PermissionCatalog.WorkflowManage);

        forms.MapPost("/{formId}/archive", async (
                string formId,
                IntakeFormService service,
                HttpContext http,
                CancellationToken ct) =>
            Ok(await service.ArchiveAsync(formId, CorrelationId(http), ct), http))
            .WithZumboPermission(PermissionCatalog.WorkflowManage);

        forms.MapGet("/{formId}/published", async (
                string formId,
                IntakeFormService service,
                HttpContext http,
                CancellationToken ct) =>
            Ok(await service.GetPublishedAsync(formId, publicAccess: false, ct), http))
            .WithZumboPermission(PermissionCatalog.WorkItemCreate);

        forms.MapPost("/{formId}/submissions", async (
                string formId,
                IntakeSubmissionService service,
                HttpContext http,
                CancellationToken ct) =>
            await SubmitAsync(formId, publicAccess: false, service, http, ct))
            .WithZumboPermission(PermissionCatalog.WorkItemCreate)
            .RequireRateLimiting("upload");

        forms.MapGet("/{formId}/submissions", async (
                string formId,
                string? state,
                int? page,
                int? pageSize,
                IntakeFormService service,
                HttpContext http,
                CancellationToken ct) =>
            Ok(await service.ListSubmissionsAsync(
                formId,
                state,
                page ?? 1,
                pageSize ?? 20,
                ct), http));

        forms.MapPost("/{formId}/submissions/{submissionId}/triage", async (
                string formId,
                string submissionId,
                TriageIntakeSubmissionRequest request,
                IntakeFormService service,
                HttpContext http,
                CancellationToken ct) =>
            Ok(await service.TriageAsync(
                formId,
                submissionId,
                request,
                CorrelationId(http),
                ct), http))
            .WithZumboPermission(PermissionCatalog.WorkItemUpdate);

        var publicForms = api.MapGroup("/intake/public/forms")
            .WithTags("PublicIntake");
        publicForms.AddEndpointFilter<WorkItemTransactionFilter>();

        publicForms.MapGet("/{publicId}", async (
                string publicId,
                IntakeFormService service,
                HttpContext http,
                CancellationToken ct) =>
            Ok(await service.GetPublishedAsync(publicId, publicAccess: true, ct), http));

        publicForms.MapPost("/{publicId}/submissions", async (
                string publicId,
                IntakeSubmissionService service,
                HttpContext http,
                CancellationToken ct) =>
            await SubmitAsync(publicId, publicAccess: true, service, http, ct))
            .RequireRateLimiting("intake-public");
    }

    private static async Task<IResult> SubmitAsync(
        string identifier,
        bool publicAccess,
        IntakeSubmissionService service,
        HttpContext http,
        CancellationToken ct)
    {
        await using var envelope = await ReadSubmissionAsync(http.Request, ct);
        var response = await service.SubmitAsync(
            identifier,
            publicAccess,
            envelope.Request,
            envelope.Attachments,
            http.Request.Headers["Idempotency-Key"].FirstOrDefault() ?? string.Empty,
            CorrelationId(http),
            ct);
        return Created(response, http);
    }

    private static async Task<SubmissionEnvelope> ReadSubmissionAsync(
        HttpRequest request,
        CancellationToken ct)
    {
        if (!request.HasFormContentType)
        {
            var jsonPayload = await request.ReadFromJsonAsync<CreateIntakeSubmissionRequest>(
                cancellationToken: ct)
                ?? throw new ValidationException("Intake submission body is required.");
            return new SubmissionEnvelope(jsonPayload, []);
        }

        var form = await request.ReadFormAsync(ct);
        var serialized = form["submission"];
        if (serialized.Count != 1 || string.IsNullOrWhiteSpace(serialized[0]))
        {
            throw new ValidationException(
                "Multipart intake submissions require one JSON 'submission' field.");
        }

        CreateIntakeSubmissionRequest multipartPayload;
        try
        {
            multipartPayload = JsonSerializer.Deserialize<CreateIntakeSubmissionRequest>(
                serialized[0]!,
                new JsonSerializerOptions(JsonSerializerDefaults.Web))
                ?? throw new JsonException();
        }
        catch (JsonException)
        {
            throw new ValidationException("Intake submission JSON is invalid.");
        }

        var attachments = new List<IntakeAttachmentUpload>();
        try
        {
            foreach (var file in form.Files)
            {
                var fieldKey = file.Name.StartsWith(
                    "attachments.",
                    StringComparison.OrdinalIgnoreCase)
                    ? file.Name["attachments.".Length..]
                    : file.Name;
                var content = new MemoryStream();
                await using (var source = file.OpenReadStream())
                {
                    await source.CopyToAsync(content, ct);
                }
                content.Position = 0;
                attachments.Add(new IntakeAttachmentUpload(
                    fieldKey,
                    content,
                    file.FileName,
                    file.ContentType,
                    file.Length));
            }
            return new SubmissionEnvelope(multipartPayload, attachments);
        }
        catch
        {
            foreach (var attachment in attachments)
            {
                await attachment.Content.DisposeAsync();
            }
            throw;
        }
    }

    private sealed class SubmissionEnvelope(
        CreateIntakeSubmissionRequest request,
        IReadOnlyCollection<IntakeAttachmentUpload> attachments) : IAsyncDisposable
    {
        public CreateIntakeSubmissionRequest Request { get; } = request;
        public IReadOnlyCollection<IntakeAttachmentUpload> Attachments { get; } = attachments;

        public async ValueTask DisposeAsync()
        {
            foreach (var attachment in Attachments)
            {
                await attachment.Content.DisposeAsync();
            }
        }
    }
}
