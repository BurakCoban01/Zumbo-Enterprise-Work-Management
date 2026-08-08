using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

internal sealed class UploadAttachmentSlice(UploadAttachmentPipeline pipeline)
{
    private const long MaxSizeBytes = 25 * 1024 * 1024;

    internal Task<WorkItemResponse> HandleAsync(
        UploadAttachmentCommand command,
        CancellationToken ct)
    {
        if (command.DeclaredSizeBytes is <= 0 or > MaxSizeBytes)
        {
            throw new ValidationException("Attachment size must be between 1 byte and 25 MB.");
        }

        if (string.IsNullOrWhiteSpace(command.FileName) || command.FileName.Length > 180)
        {
            throw new ValidationException(
                "Attachment file name is required and cannot exceed 180 characters.");
        }

        return pipeline.UploadAsync(command, MaxSizeBytes, ct);
    }
}
