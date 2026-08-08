using Zumbo.Api.Presentation.Endpoints.WorkItems.Attachments;

internal static partial class WorkItemEndpoints
{
    private static void MapPostByIdAttachmentsUpload(RouteGroupBuilder group) => UploadAttachmentEndpoint.Map(group);

    private static void MapGetByIdAttachments(RouteGroupBuilder group) => ListAttachmentsEndpoint.Map(group);

    private static void MapGetByIdAttachmentsByAttachmentIdDownload(RouteGroupBuilder group) => DownloadAttachmentEndpoint.Map(group);

    private static void MapGetByIdAttachmentsByAttachmentIdPreview(RouteGroupBuilder group) => PreviewAttachmentEndpoint.Map(group);

    private static void MapDeleteByIdAttachmentsByAttachmentId(RouteGroupBuilder group) => DeleteAttachmentEndpoint.Map(group);
}
