using Zumbo.SharedKernel;

namespace Zumbo.Modules.Boards.Application.Features.Lifecycle;

public static class ArchiveBoardValidator
{
    public static void Validate(ArchiveBoardCommand command)
    {
        if (string.IsNullOrWhiteSpace(command.BoardId))
        {
            throw new ValidationException("Board id is required.");
        }
    }
}
