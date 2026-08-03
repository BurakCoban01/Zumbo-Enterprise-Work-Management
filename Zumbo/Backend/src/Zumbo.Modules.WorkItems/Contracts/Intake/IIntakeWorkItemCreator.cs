namespace Zumbo.Modules.WorkItems;

public interface IIntakeWorkItemCreator
{
    Task<WorkItemResponse> CreateAsync(
        IntakeWorkItemCreation creation,
        CancellationToken ct);
}
