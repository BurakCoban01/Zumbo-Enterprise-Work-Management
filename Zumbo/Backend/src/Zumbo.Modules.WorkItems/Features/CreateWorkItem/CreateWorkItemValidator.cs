using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

public sealed class CreateWorkItemValidator
{
    public static void Validate(CreateWorkItemRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.ProjectId)
            || string.IsNullOrWhiteSpace(request.BoardId)
            || string.IsNullOrWhiteSpace(request.Title))
        {
            throw new ValidationException("Project id, board id and title are required.");
        }

        if (request.Title.Length > 200)
        {
            throw new ValidationException("Work item title cannot exceed 200 characters.");
        }
    }
}
