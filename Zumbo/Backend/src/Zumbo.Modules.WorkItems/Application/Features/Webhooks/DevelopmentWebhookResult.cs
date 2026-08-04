namespace Zumbo.Modules.WorkItems;

public sealed record DevelopmentWebhookResult(
    string Status,
    int AppliedLinks,
    bool Duplicate);
