using Zumbo.Modules.WorkItems;
using Zumbo.SharedKernel;

namespace Zumbo.Api.Presentation.Binding;

public static class DevelopmentWebhookRequestReader
{
    public static async Task<DevelopmentWebhookRequest> ReadAsync(HttpRequest request, CancellationToken cancellationToken)
    {
        var payload = await ReadPayloadAsync(request, cancellationToken);
        return new DevelopmentWebhookRequest(
            Header(request, 200, "X-GitHub-Delivery", "webhook-id", "Idempotency-Key"),
            Header(request, 120, "X-GitHub-Event", "X-Gitlab-Event"),
            OptionalHeader(request, 32, "webhook-timestamp"),
            Header(request, 2_048, "X-Hub-Signature-256", "webhook-signature"),
            payload);
    }

    private static async Task<byte[]> ReadPayloadAsync(HttpRequest request, CancellationToken cancellationToken)
    {
        if (request.ContentLength > DevelopmentIntegrationLimits.MaximumWebhookPayloadBytes)
        {
            throw new ValidationException("Development webhook payload is too large.");
        }

        using var destination = new MemoryStream();
        var buffer = new byte[16_384];
        while (true)
        {
            var read = await request.Body.ReadAsync(buffer.AsMemory(), cancellationToken);
            if (read == 0) break;
            if (destination.Length + read > DevelopmentIntegrationLimits.MaximumWebhookPayloadBytes)
            {
                throw new ValidationException("Development webhook payload is too large.");
            }
            await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }
        return destination.ToArray();
    }

    private static string Header(HttpRequest request, int maximum, params string[] names) =>
        OptionalHeader(request, maximum, names) ?? string.Empty;

    private static string? OptionalHeader(HttpRequest request, int maximum, params string[] names)
    {
        foreach (var name in names)
        {
            var value = request.Headers[name].ToString().Trim();
            if (value.Length == 0) continue;
            if (value.Length > maximum)
            {
                throw new ValidationException("Development webhook header is too large.");
            }
            return value;
        }
        return null;
    }
}
