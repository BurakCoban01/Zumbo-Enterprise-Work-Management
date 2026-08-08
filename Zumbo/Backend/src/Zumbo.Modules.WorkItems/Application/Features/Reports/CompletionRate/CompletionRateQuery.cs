namespace Zumbo.Modules.WorkItems;

public sealed record CompletionRateQuery(string ProjectId, DateOnly? From, DateOnly? To);
