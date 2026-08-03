namespace Zumbo.Modules.WorkItems;

public sealed record DevelopmentConnectionReceipt(
    DevelopmentConnectionResponse Connection,
    string WebhookSecret);
