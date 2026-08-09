using Microsoft.AspNetCore.Mvc;
using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.Modules.Projects;
using Zumbo.Modules.Projects.Application.Features.Knowledge;
using Zumbo.SharedKernel;

using static ApiEndpointResults;

internal static class KnowledgeEndpoints
{
    internal static IServiceCollection AddKnowledgeModule(
        this IServiceCollection services)
    {
        services.AddScoped<IKnowledgeDirectory, KnowledgeDirectoryAdapter>();
        services.AddScoped<IKnowledgeAuditWriter, KnowledgeAuditWriterAdapter>();
        services.AddScoped<KnowledgeService>();
        services.AddScoped<GetKnowledgeDocumentHandler>(provider => new GetKnowledgeDocumentHandler(
            provider.GetRequiredService<IDocumentRepository<KnowledgeDocument>>(),
            provider.GetRequiredService<IKnowledgeDirectory>(),
            provider.GetRequiredService<ICurrentUser>()));
        services.AddScoped<GetKnowledgeVersionHandler>(provider => new GetKnowledgeVersionHandler(
            provider.GetRequiredService<IDocumentRepository<KnowledgeDocument>>(),
            provider.GetRequiredService<IKnowledgeDirectory>(),
            provider.GetRequiredService<ICurrentUser>()));
        services.AddScoped<GetKnowledgeLinkOptionsHandler>(provider => new GetKnowledgeLinkOptionsHandler(
            provider.GetRequiredService<IKnowledgeDirectory>(),
            provider.GetRequiredService<ICurrentUser>()));
        services.AddScoped<SearchKnowledgeDocumentsHandler>(provider => new SearchKnowledgeDocumentsHandler(
            provider.GetRequiredService<IDocumentRepository<KnowledgeDocument>>(),
            provider.GetRequiredService<IKnowledgeDirectory>(),
            provider.GetRequiredService<ICurrentUser>()));
        services.AddScoped<AddKnowledgeCommentHandler>(provider => new AddKnowledgeCommentHandler(
            provider.GetRequiredService<IDocumentRepository<KnowledgeDocument>>(),
            provider.GetRequiredService<IKnowledgeDirectory>(),
            provider.GetRequiredService<IKnowledgeAuditWriter>(),
            provider.GetRequiredService<ICurrentUser>(),
            provider.GetRequiredService<IClock>(),
            provider.GetService<IExpectedVersionAccessor>()));
        services.AddScoped<ResolveKnowledgeCommentHandler>(provider => new ResolveKnowledgeCommentHandler(
            provider.GetRequiredService<IDocumentRepository<KnowledgeDocument>>(),
            provider.GetRequiredService<IKnowledgeDirectory>(),
            provider.GetRequiredService<IKnowledgeAuditWriter>(),
            provider.GetRequiredService<ICurrentUser>(),
            provider.GetRequiredService<IClock>(),
            provider.GetService<IExpectedVersionAccessor>()));
        services.AddScoped<CreateKnowledgeDocumentHandler>(provider => new CreateKnowledgeDocumentHandler(
            provider.GetRequiredService<IDocumentRepository<KnowledgeDocument>>(),
            provider.GetRequiredService<IKnowledgeDirectory>(),
            provider.GetRequiredService<IKnowledgeAuditWriter>(),
            provider.GetRequiredService<ICurrentUser>(),
            provider.GetRequiredService<IClock>()));
        services.AddScoped<AddKnowledgeVersionHandler>(provider => new AddKnowledgeVersionHandler(
            provider.GetRequiredService<IDocumentRepository<KnowledgeDocument>>(),
            provider.GetRequiredService<IKnowledgeDirectory>(),
            provider.GetRequiredService<IKnowledgeAuditWriter>(),
            provider.GetRequiredService<ICurrentUser>(),
            provider.GetRequiredService<IClock>(),
            provider.GetService<IExpectedVersionAccessor>()));
        services.AddScoped<ArchiveKnowledgeDocumentHandler>(provider => new ArchiveKnowledgeDocumentHandler(
            provider.GetRequiredService<IDocumentRepository<KnowledgeDocument>>(),
            provider.GetRequiredService<IKnowledgeDirectory>(),
            provider.GetRequiredService<IKnowledgeAuditWriter>(),
            provider.GetRequiredService<ICurrentUser>(),
            provider.GetRequiredService<IClock>(),
            provider.GetService<IExpectedVersionAccessor>()));
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
            [FromServices] SearchKnowledgeDocumentsHandler handler,
            HttpContext http,
            CancellationToken ct) =>
            Ok(await handler.HandleAsync(
                new SearchKnowledgeDocumentsQuery(
                    query,
                    scopeType,
                    scopeId,
                    includeArchived ?? false,
                    page ?? 1,
                    pageSize ?? 50),
                ct), http))
            .RequireRateLimiting("search");

        group.MapGet("/{documentId}", async (
            string documentId,
            bool? includeArchived,
            [FromServices] GetKnowledgeDocumentHandler handler,
            HttpContext http,
            CancellationToken ct) =>
            Ok(await handler.HandleAsync(
                new GetKnowledgeDocumentQuery(documentId, includeArchived ?? false),
                ct), http));

        group.MapGet("/scope-link-options", async (
            string scopeType,
            string scopeId,
            string? query,
            [FromServices] GetKnowledgeLinkOptionsHandler handler,
            HttpContext http,
            CancellationToken ct) =>
            Ok(await handler.HandleAsync(
                new GetKnowledgeLinkOptionsQuery(scopeType, scopeId, query),
                ct), http))
            .RequireRateLimiting("search");

        group.MapGet("/{documentId}/versions/{number:int}", async (
            string documentId,
            int number,
            [FromServices] GetKnowledgeVersionHandler handler,
            HttpContext http,
            CancellationToken ct) =>
            Ok(await handler.HandleAsync(
                new GetKnowledgeVersionQuery(documentId, number),
                ct), http));

        group.MapPost("", async (
            CreateKnowledgeDocumentRequest request,
            [FromServices] CreateKnowledgeDocumentHandler handler,
            HttpContext http,
            CancellationToken ct) =>
            Ok(await handler.HandleAsync(
                new CreateKnowledgeDocumentCommand(request, CorrelationId(http)),
                ct), http));

        group.MapPut("/{documentId}", async (
            string documentId,
            CreateKnowledgeVersionRequest request,
            [FromServices] AddKnowledgeVersionHandler handler,
            HttpContext http,
            CancellationToken ct) =>
            Ok(await handler.HandleAsync(
                new AddKnowledgeVersionCommand(documentId, request, CorrelationId(http)),
                ct), http));

        group.MapPost("/{documentId}/comments", async (
            string documentId,
            AddKnowledgeCommentRequest request,
            [FromServices] AddKnowledgeCommentHandler handler,
            HttpContext http,
            CancellationToken ct) =>
            Ok(await handler.HandleAsync(
                new AddKnowledgeCommentCommand(
                    documentId,
                    request,
                    CorrelationId(http)),
                ct), http));

        group.MapPatch("/{documentId}/comments/{commentId}/resolve", async (
            string documentId,
            string commentId,
            [FromServices] ResolveKnowledgeCommentHandler handler,
            HttpContext http,
            CancellationToken ct) =>
            Ok(await handler.HandleAsync(
                new ResolveKnowledgeCommentCommand(
                    documentId,
                    commentId,
                    CorrelationId(http)),
                ct), http));

        group.MapDelete("/{documentId}", async (
            string documentId,
            [FromServices] ArchiveKnowledgeDocumentHandler handler,
            HttpContext http,
            CancellationToken ct) =>
        {
            await handler.HandleAsync(
                new ArchiveKnowledgeDocumentCommand(documentId, CorrelationId(http)),
                ct);
            return Ok(new { archived = true }, http);
        });
    }
}
