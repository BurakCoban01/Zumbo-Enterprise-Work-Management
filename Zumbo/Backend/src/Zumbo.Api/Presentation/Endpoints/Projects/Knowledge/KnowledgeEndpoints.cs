using Microsoft.AspNetCore.Mvc;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.Modules.Projects;

using static ApiEndpointResults;

internal static class KnowledgeEndpoints
{
    internal static IServiceCollection AddKnowledgeModule(
        this IServiceCollection services)
    {
        services.AddScoped<IKnowledgeDirectory, KnowledgeDirectoryAdapter>();
        services.AddScoped<IKnowledgeAuditWriter, KnowledgeAuditWriterAdapter>();
        services.AddScoped<KnowledgeService>();
        return services;
    }

    internal static void MapKnowledgeEndpoints(this RouteGroupBuilder api)
    {
        var group = api.MapGroup("/knowledge-documents")
            .WithTags("Knowledge")
            .RequireAuthorization()
            .WithZumboPermission(PermissionCatalog.ProjectView);

        group.MapGet("", async (
            string? query,
            string? scopeType,
            string? scopeId,
            bool? includeArchived,
            int? page,
            int? pageSize,
            [FromServices] KnowledgeService service,
            HttpContext http,
            CancellationToken ct) =>
            Ok(await service.SearchAsync(
                query,
                scopeType,
                scopeId,
                includeArchived ?? false,
                page ?? 1,
                pageSize ?? 50,
                ct), http))
            .RequireRateLimiting("search");

        group.MapGet("/{documentId}", async (
            string documentId,
            bool? includeArchived,
            [FromServices] KnowledgeService service,
            HttpContext http,
            CancellationToken ct) =>
            Ok(await service.GetAsync(
                documentId,
                includeArchived ?? false,
                ct), http));

        group.MapGet("/scope-link-options", async (
            string scopeType,
            string scopeId,
            string? query,
            [FromServices] KnowledgeService service,
            HttpContext http,
            CancellationToken ct) =>
            Ok(await service.GetLinkOptionsAsync(
                scopeType,
                scopeId,
                query,
                ct), http))
            .RequireRateLimiting("search");

        group.MapGet("/{documentId}/versions/{number:int}", async (
            string documentId,
            int number,
            [FromServices] KnowledgeService service,
            HttpContext http,
            CancellationToken ct) =>
            Ok(await service.GetVersionAsync(documentId, number, ct), http));

        group.MapPost("", async (
            CreateKnowledgeDocumentRequest request,
            [FromServices] KnowledgeService service,
            HttpContext http,
            CancellationToken ct) =>
            Ok(await service.CreateAsync(
                request,
                CorrelationId(http),
                ct), http));

        group.MapPut("/{documentId}", async (
            string documentId,
            CreateKnowledgeVersionRequest request,
            [FromServices] KnowledgeService service,
            HttpContext http,
            CancellationToken ct) =>
            Ok(await service.AddVersionAsync(
                documentId,
                request,
                CorrelationId(http),
                ct), http));

        group.MapPost("/{documentId}/comments", async (
            string documentId,
            AddKnowledgeCommentRequest request,
            [FromServices] KnowledgeService service,
            HttpContext http,
            CancellationToken ct) =>
            Ok(await service.AddCommentAsync(
                documentId,
                request,
                CorrelationId(http),
                ct), http));

        group.MapPatch("/{documentId}/comments/{commentId}/resolve", async (
            string documentId,
            string commentId,
            [FromServices] KnowledgeService service,
            HttpContext http,
            CancellationToken ct) =>
            Ok(await service.ResolveCommentAsync(
                documentId,
                commentId,
                CorrelationId(http),
                ct), http));

        group.MapDelete("/{documentId}", async (
            string documentId,
            [FromServices] KnowledgeService service,
            HttpContext http,
            CancellationToken ct) =>
        {
            await service.ArchiveAsync(
                documentId,
                CorrelationId(http),
                ct);
            return Ok(new { archived = true }, http);
        });
    }
}
