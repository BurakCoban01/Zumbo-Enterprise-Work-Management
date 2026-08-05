using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

internal sealed class AddWorkLogSlice(WorkLogMutationPipeline pipeline)
{
    internal async Task<WorkItemResponse> HandleAsync(AddWorkLogCommand command, CancellationToken ct)
    {
        if (command.Request.Hours <= 0 || command.Request.Hours > 24)
        {
            throw new ValidationException("Work log hours must be between 0 and 24.");
        }

        var workItem = await pipeline.LoadForCreateAsync(command.Id, ct);
        return await pipeline.AppendAsync(workItem, command.Request, ct);
    }
}
