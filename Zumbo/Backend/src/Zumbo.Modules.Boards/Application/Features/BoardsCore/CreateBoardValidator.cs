using Zumbo.SharedKernel;

namespace Zumbo.Modules.Boards;

public sealed class CreateBoardValidator
{
    public static void Validate(CreateBoardRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.ProjectId) || string.IsNullOrWhiteSpace(request.Name))
        {
            throw new ValidationException("Project id and board name are required.");
        }
    }
}
