using Zumbo.Api.Composition.Modules.Boards;
using Zumbo.Modules.Boards;
using Zumbo.Modules.Boards.Application.Features.BoardsCore;
using Zumbo.Modules.Boards.Application.Features.ColumnOrdering;
using Zumbo.Modules.Boards.Application.Features.Columns;
using Zumbo.Modules.Boards.Application.Features.Lifecycle;
using Zumbo.Modules.Boards.Application.Features.Swimlanes;
using Zumbo.Modules.Boards.Application.Features.Views;
using Zumbo.BuildingBlocks.Application.Security;

using static ApiEndpointResults;

internal static class BoardsEndpoints
{
    internal static IServiceCollection AddBoardsModule(this IServiceCollection services) =>
        services.AddBoardServices();

    internal static void MapBoardsEndpoints(this RouteGroupBuilder api)
    {
        var group = api.MapGroup("/boards").WithTags("Boards").RequireAuthorization()
            .WithZumboPermission(PermissionCatalog.BoardView);

        group.MapGet("/by-project/{projectId}", async (string projectId, bool? archived, ListBoardsByProjectHandler handler, HttpContext http, CancellationToken ct) =>
            Ok(await handler.HandleAsync(new ListBoardsByProjectQuery(projectId, archived ?? false), ct), http));

        group.MapPost("/", async (CreateBoardRequest request, CreateBoardHandler handler, HttpContext http, CancellationToken ct) =>
            Created(await handler.HandleAsync(request, CorrelationId(http), ct), http))
            .WithZumboPermission(PermissionCatalog.BoardManage);

        group.MapPut("/{boardId}", async (string boardId, UpdateBoardRequest request, UpdateBoardHandler handler, HttpContext http, CancellationToken ct) =>
            Ok(await handler.HandleAsync(boardId, request, CorrelationId(http), ct), http))
            .WithZumboPermission(PermissionCatalog.BoardManage);

        group.MapPatch("/{boardId}/swimlane", async (string boardId, UpdateSwimlaneRequest request, UpdateSwimlaneHandler handler, HttpContext http, CancellationToken ct) =>
            Ok(await handler.HandleAsync(boardId, request, CorrelationId(http), ct), http))
            .WithZumboPermission(PermissionCatalog.BoardManage);

        group.MapPost("/{boardId}/views", async (string boardId, CreateBoardViewRequest request, CreateViewHandler handler, HttpContext http, CancellationToken ct) =>
            Ok(await handler.HandleAsync(boardId, request, CorrelationId(http), ct), http))
            .WithZumboPermission(PermissionCatalog.BoardManage);

        group.MapPut("/{boardId}/views/{viewId}", async (string boardId, string viewId, UpdateBoardViewRequest request, UpdateViewHandler handler, HttpContext http, CancellationToken ct) =>
            Ok(await handler.HandleAsync(boardId, viewId, request, CorrelationId(http), ct), http))
            .WithZumboPermission(PermissionCatalog.BoardManage);

        group.MapDelete("/{boardId}/views/{viewId}", async (string boardId, string viewId, DeleteViewHandler handler, HttpContext http, CancellationToken ct) =>
            Ok(await handler.HandleAsync(boardId, viewId, CorrelationId(http), ct), http))
            .WithZumboPermission(PermissionCatalog.BoardManage);

        group.MapPost("/{boardId}/columns", async (string boardId, CreateColumnRequest request, AddColumnHandler handler, HttpContext http, CancellationToken ct) =>
            Ok(await handler.HandleAsync(boardId, request, CorrelationId(http), ct), http))
            .WithZumboPermission(PermissionCatalog.BoardManage);

        group.MapPut("/{boardId}/columns/{columnId}", async (
            string boardId,
            string columnId,
            UpdateColumnRequest request,
            UpdateColumnHandler handler,
            HttpContext http,
            CancellationToken ct) =>
            Ok(await handler.HandleAsync(boardId, columnId, request, CorrelationId(http), ct), http))
            .WithZumboPermission(PermissionCatalog.BoardManage);

        group.MapPut("/{boardId}/columns/reorder", async (string boardId, ReorderColumnsRequest request, ReorderColumnsHandler handler, HttpContext http, CancellationToken ct) =>
            Ok(await handler.HandleAsync(boardId, request, CorrelationId(http), ct), http))
            .WithZumboPermission(PermissionCatalog.BoardManage);

        group.MapPut("/{boardId}/workflow-mapping", async (
            string boardId,
            ConfigureBoardWorkflowMappingRequest request,
            BoardWorkflowMappingService service,
            HttpContext http,
            CancellationToken ct) =>
            Ok(await service.ConfigureAsync(boardId, request, CorrelationId(http), ct), http))
            .WithZumboPermission(PermissionCatalog.BoardManage);

        group.MapDelete("/{boardId}/columns/{columnId}", async (string boardId, string columnId, DeleteColumnHandler handler, HttpContext http, CancellationToken ct) =>
            Ok(await handler.HandleAsync(new DeleteColumnCommand(boardId, columnId, CorrelationId(http)), ct), http))
            .WithZumboPermission(PermissionCatalog.BoardManage);

        group.MapDelete("/{boardId}", async (string boardId, ArchiveBoardHandler handler, HttpContext http, CancellationToken ct) =>
        {
            await handler.HandleAsync(new ArchiveBoardCommand(boardId, CorrelationId(http)), ct);
            return Ok(new { archived = true }, http);
        }).WithZumboPermission(PermissionCatalog.BoardManage);

        group.MapPost("/{boardId}/restore", async (string boardId, RestoreBoardHandler handler, HttpContext http, CancellationToken ct) =>
            Ok(await handler.HandleAsync(new RestoreBoardCommand(boardId, CorrelationId(http)), ct), http))
            .WithZumboPermission(PermissionCatalog.BoardManage);
    }
}
