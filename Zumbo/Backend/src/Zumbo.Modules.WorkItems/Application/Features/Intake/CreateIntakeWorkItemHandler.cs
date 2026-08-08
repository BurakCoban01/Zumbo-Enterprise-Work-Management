using Zumbo.Modules.WorkItems;

namespace Zumbo.Modules.WorkItems.Application.Features.Intake;

public sealed class CreateIntakeWorkItemHandler(CreateWorkItemHandler createWorkItemHandler)
    : IIntakeWorkItemCreator
{
    public Task<WorkItemResponse> CreateAsync(
        IntakeWorkItemCreation creation,
        CancellationToken ct) =>
        createWorkItemHandler.CreateAsync(creation, ct);
}
