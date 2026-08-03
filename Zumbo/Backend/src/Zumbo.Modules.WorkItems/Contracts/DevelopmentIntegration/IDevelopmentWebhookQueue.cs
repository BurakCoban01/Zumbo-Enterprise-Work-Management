namespace Zumbo.Modules.WorkItems;

public interface IDevelopmentWebhookQueue
{
    Task EnqueueAsync(DevelopmentWebhookEvent message, CancellationToken ct);
}
