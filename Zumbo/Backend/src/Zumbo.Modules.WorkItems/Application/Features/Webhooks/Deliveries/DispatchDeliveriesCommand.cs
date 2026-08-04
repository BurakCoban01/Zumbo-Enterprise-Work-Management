namespace Zumbo.Modules.WorkItems.Application.Features.Webhooks.Deliveries;

public sealed record DispatchDeliveriesCommand(int BatchSize, string WorkerId);
