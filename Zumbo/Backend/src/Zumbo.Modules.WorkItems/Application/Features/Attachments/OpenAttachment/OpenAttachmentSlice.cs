namespace Zumbo.Modules.WorkItems;

internal sealed class OpenAttachmentSlice(OpenAttachmentPipeline pipeline)
{
    internal Task<AttachmentFile> HandleAsync(OpenAttachmentQuery query, CancellationToken ct) =>
        pipeline.OpenAsync(query.Id, query.AttachmentId, ct);
}
