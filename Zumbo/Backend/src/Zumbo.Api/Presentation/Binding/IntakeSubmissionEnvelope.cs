using Zumbo.Modules.WorkItems;

namespace Zumbo.Api.Presentation.Binding;

public sealed class IntakeSubmissionEnvelope(
    CreateIntakeSubmissionRequest request,
    IReadOnlyCollection<IntakeAttachmentUpload> attachments) : IAsyncDisposable
{
    public CreateIntakeSubmissionRequest Request { get; } = request;
    public IReadOnlyCollection<IntakeAttachmentUpload> Attachments { get; } = attachments;

    public async ValueTask DisposeAsync()
    {
        foreach (var attachment in Attachments)
        {
            await attachment.Content.DisposeAsync();
        }
    }
}
