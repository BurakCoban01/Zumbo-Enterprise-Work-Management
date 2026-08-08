namespace Zumbo.Modules.WorkItems;

public sealed record FlowTimeQuery(string ProjectId, DateOnly? From, DateOnly? To);
