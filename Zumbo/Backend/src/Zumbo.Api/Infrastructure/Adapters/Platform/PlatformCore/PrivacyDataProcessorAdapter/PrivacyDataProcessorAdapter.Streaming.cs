using System.Linq.Expressions;
using System.Text;
using System.Text.Json;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.Modules.Audit;
using Zumbo.Modules.Identity;
using Zumbo.Modules.Notifications;
using Zumbo.Modules.Organizations;
using Zumbo.Modules.Projects;
using Zumbo.Modules.Teams;
using Zumbo.Modules.WorkItems;
using Zumbo.SharedKernel;

public sealed partial class PrivacyDataProcessorAdapter{

    private sealed record PrivacyStreamLine(
        string Kind,
        string? Category,
        string ResourceId,
        string? Detail,
        UserProfileResponse? Profile);

    private static async Task<long> WriteDocumentsAsync<TDocument>(
        IDocumentRepository<TDocument> repository,
        Expression<Func<TDocument, bool>> filter,
        string category,
        Func<TDocument, IEnumerable<PrivacyDataReference>> select,
        StreamWriter writer,
        CancellationToken ct)
        where TDocument : class, IDocument
    {
        return await PrivacyDocumentAccess.WriteDocumentsAsync(
            repository,
            filter,
            select,
            reference => WriteReferenceAsync(writer, category, reference, ct),
            ct);
    }

    public async Task<long> WriteExportAsync(
        string userId,
        string organizationId,
        UserProfileResponse profile,
        Stream destination,
        CancellationToken ct)
    {
        return await streamExport.WriteExportAsync(userId, organizationId, profile, destination, ct);
    }

    private static Task WriteReferenceAsync(
        StreamWriter writer,
        string category,
        PrivacyDataReference reference,
        CancellationToken ct) =>
        PrivacyStreamSerialization.WriteReferenceAsync(writer, category, reference, StreamJson, ct);

    private static Task WriteLineAsync(
        StreamWriter writer,
        PrivacyStreamLine line,
        CancellationToken ct) =>
        PrivacyStreamSerialization.WriteLineAsync(writer, line, StreamJson, ct);
}
