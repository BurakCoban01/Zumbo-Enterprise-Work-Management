using Microsoft.Extensions.Options;
using Zumbo.Modules.Boards;
using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

using static ApiEndpointResults;

internal static class BoardsEndpoints
{
    internal static IServiceCollection AddBoardsModule(this IServiceCollection services)
    {
        services.AddScoped<IBoardProjectAccessChecker, BoardProjectAccessCheckerAdapter>();
        services.AddScoped<IBoardAuditWriter, BoardAuditWriterAdapter>();
        services.AddScoped<IBoardWorkflowCatalog, BoardWorkflowCatalogAdapter>();
        services.AddScoped<BoardPolicyAdapter>();
        services.AddScoped<IBoardColumnUsageChecker>(provider => provider.GetRequiredService<BoardPolicyAdapter>());
        services.AddScoped<BoardService>();
        services.AddScoped<BoardWorkflowMappingService>();
        services.AddScoped<CreateBoardHandler>(provider => new CreateBoardHandler(
            provider.GetRequiredService<IDocumentRepository<BoardDocument>>(),
            provider.GetRequiredService<IBoardProjectAccessChecker>(),
            provider.GetRequiredService<IDistributedLockProvider>(),
            provider.GetRequiredService<IOptions<DistributedLockOptions>>(),
            provider.GetRequiredService<IClock>(),
            provider.GetRequiredService<ICurrentUser>(),
            provider.GetRequiredService<IBoardAuditWriter>()));
        services.AddScoped<ListBoardsByProjectHandler>(provider => new ListBoardsByProjectHandler(
            provider.GetRequiredService<IDocumentRepository<BoardDocument>>(),
            provider.GetRequiredService<IBoardProjectAccessChecker>(),
            provider.GetRequiredService<ICurrentUser>()));
        return services;
    }

    internal static void MapBoardsEndpoints(this RouteGroupBuilder api)
    {
        var group = api.MapGroup("/boards").WithTags("Boards").RequireAuthorization()
            .WithZumboPermission(PermissionCatalog.BoardView);

        group.MapGet("/by-project/{projectId}", async (string projectId, bool? archived, ListBoardsByProjectHandler handler, HttpContext http, CancellationToken ct) =>
            Ok(await handler.HandleAsync(new ListBoardsByProjectQuery(projectId, archived ?? false), ct), http));

        group.MapPost("/", async (CreateBoardRequest request, CreateBoardHandler handler, HttpContext http, CancellationToken ct) =>
            Created(await handler.HandleAsync(request, CorrelationId(http), ct), http))
            .WithZumboPermission(PermissionCatalog.BoardManage);

        group.MapPut("/{boardId}", async (string boardId, UpdateBoardRequest request, BoardService service, HttpContext http, CancellationToken ct) =>
            Ok(await service.UpdateAsync(boardId, request, CorrelationId(http), ct), http))
            .WithZumboPermission(PermissionCatalog.BoardManage);

        group.MapPatch("/{boardId}/swimlane", async (string boardId, UpdateSwimlaneRequest request, BoardService service, HttpContext http, CancellationToken ct) =>
            Ok(await service.UpdateSwimlaneAsync(boardId, request, CorrelationId(http), ct), http))
            .WithZumboPermission(PermissionCatalog.BoardManage);

        group.MapPost("/{boardId}/views", async (string boardId, CreateBoardViewRequest request, BoardService service, HttpContext http, CancellationToken ct) =>
            Ok(await service.CreateViewAsync(boardId, request, CorrelationId(http), ct), http))
            .WithZumboPermission(PermissionCatalog.BoardManage);

        group.MapPut("/{boardId}/views/{viewId}", async (string boardId, string viewId, UpdateBoardViewRequest request, BoardService service, HttpContext http, CancellationToken ct) =>
            Ok(await service.UpdateViewAsync(boardId, viewId, request, CorrelationId(http), ct), http))
            .WithZumboPermission(PermissionCatalog.BoardManage);

        group.MapDelete("/{boardId}/views/{viewId}", async (string boardId, string viewId, BoardService service, HttpContext http, CancellationToken ct) =>
            Ok(await service.DeleteViewAsync(boardId, viewId, CorrelationId(http), ct), http))
            .WithZumboPermission(PermissionCatalog.BoardManage);

        group.MapPost("/{boardId}/columns", async (string boardId, CreateColumnRequest request, BoardService service, HttpContext http, CancellationToken ct) =>
            Ok(await service.AddColumnAsync(boardId, request, CorrelationId(http), ct), http))
            .WithZumboPermission(PermissionCatalog.BoardManage);

        group.MapPut("/{boardId}/columns/{columnId}", async (
            string boardId,
            string columnId,
            UpdateColumnRequest request,
            BoardService service,
            HttpContext http,
            CancellationToken ct) =>
            Ok(await service.UpdateColumnAsync(boardId, columnId, request, CorrelationId(http), ct), http))
            .WithZumboPermission(PermissionCatalog.BoardManage);

        group.MapPut("/{boardId}/columns/reorder", async (string boardId, ReorderColumnsRequest request, BoardService service, HttpContext http, CancellationToken ct) =>
            Ok(await service.ReorderColumnsAsync(boardId, request, CorrelationId(http), ct), http))
            .WithZumboPermission(PermissionCatalog.BoardManage);

        group.MapPut("/{boardId}/workflow-mapping", async (
            string boardId,
            ConfigureBoardWorkflowMappingRequest request,
            BoardWorkflowMappingService service,
            HttpContext http,
            CancellationToken ct) =>
            Ok(await service.ConfigureAsync(boardId, request, CorrelationId(http), ct), http))
            .WithZumboPermission(PermissionCatalog.BoardManage);

        group.MapDelete("/{boardId}/columns/{columnId}", async (string boardId, string columnId, BoardService service, HttpContext http, CancellationToken ct) =>
            Ok(await service.DeleteColumnAsync(boardId, columnId, CorrelationId(http), ct), http))
            .WithZumboPermission(PermissionCatalog.BoardManage);

        group.MapDelete("/{boardId}", async (string boardId, BoardService service, HttpContext http, CancellationToken ct) =>
        {
            await service.ArchiveAsync(boardId, CorrelationId(http), ct);
            return Ok(new { archived = true }, http);
        }).WithZumboPermission(PermissionCatalog.BoardManage);

        group.MapPost("/{boardId}/restore", async (string boardId, BoardService service, HttpContext http, CancellationToken ct) =>
            Ok(await service.RestoreAsync(boardId, CorrelationId(http), ct), http))
            .WithZumboPermission(PermissionCatalog.BoardManage);
    }
}
