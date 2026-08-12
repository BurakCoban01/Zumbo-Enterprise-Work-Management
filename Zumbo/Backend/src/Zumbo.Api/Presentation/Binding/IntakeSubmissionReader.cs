using System.Text.Json;
using Zumbo.Modules.WorkItems;
using Zumbo.SharedKernel;

namespace Zumbo.Api.Presentation.Binding;

public static class IntakeSubmissionReader
{
    public static async Task<IntakeSubmissionEnvelope> ReadAsync(HttpRequest request, CancellationToken cancellationToken)
    {
        if (!request.HasFormContentType)
        {
            var jsonPayload = await request.ReadFromJsonAsync<CreateIntakeSubmissionRequest>(cancellationToken: cancellationToken)
                ?? throw new ValidationException("Intake submission body is required.");
            return new IntakeSubmissionEnvelope(jsonPayload, []);
        }

        var form = await request.ReadFormAsync(cancellationToken);
        var serialized = form["submission"];
        if (serialized.Count != 1 || string.IsNullOrWhiteSpace(serialized[0]))
        {
            throw new ValidationException("Multipart intake submissions require one JSON 'submission' field.");
        }

        CreateIntakeSubmissionRequest multipartPayload;
        try
        {
            multipartPayload = JsonSerializer.Deserialize<CreateIntakeSubmissionRequest>(
                serialized[0]!, new JsonSerializerOptions(JsonSerializerDefaults.Web)) ?? throw new JsonException();
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
                var fieldKey = file.Name.StartsWith("attachments.", StringComparison.OrdinalIgnoreCase)
                    ? file.Name["attachments.".Length..]
                    : file.Name;
                var content = new MemoryStream();
                await using (var source = file.OpenReadStream())
                {
                    await source.CopyToAsync(content, cancellationToken);
                }
                content.Position = 0;
                attachments.Add(new IntakeAttachmentUpload(fieldKey, content, file.FileName, file.ContentType, file.Length));
            }
            return new IntakeSubmissionEnvelope(multipartPayload, attachments);
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
}
