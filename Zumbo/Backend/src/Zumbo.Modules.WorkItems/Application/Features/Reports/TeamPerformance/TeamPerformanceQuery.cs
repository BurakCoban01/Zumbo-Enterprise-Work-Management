namespace Zumbo.Modules.WorkItems;

public sealed record TeamPerformanceQuery(string ProjectId, DateOnly? From, DateOnly? To);
