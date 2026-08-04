namespace Zumbo.Modules.WorkItems.Application.Features.CapacityPlanning.Scenarios;

internal static class ScenarioAllocationMapper
{
    public static CapacityAllocationDocument ToDocument(
        CapacityAllocationRequest item) => new()
    {
        Id = item.Id!,
        UserId = item.UserId,
        ProjectId = item.ProjectId,
        StartDateUtc = UtcDay(item.StartDate),
        EndDateUtc = UtcDay(item.EndDate),
        Percent = item.Percent
    };

    private static DateTimeOffset UtcDay(DateOnly value) =>
        new(value.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
}
