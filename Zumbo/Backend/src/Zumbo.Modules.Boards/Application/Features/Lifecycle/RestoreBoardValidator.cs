using Zumbo.SharedKernel;

namespace Zumbo.Modules.Boards.Application.Features.Lifecycle;

public static class RestoreBoardValidator
{
    public static void Validate(RestoreBoardCommand command)
    {
        if (string.IsNullOrWhiteSpace(command.BoardId))
        {
            throw new ValidationException("Board id is required.");
        }
    }
}
