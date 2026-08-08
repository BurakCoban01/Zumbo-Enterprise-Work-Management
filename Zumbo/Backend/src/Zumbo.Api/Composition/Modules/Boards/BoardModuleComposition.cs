using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.Modules.Boards;
using Zumbo.Modules.Boards.Application.Features.BoardsCore;
using Zumbo.Modules.Boards.Application.Features.ColumnOrdering;
using Zumbo.Modules.Boards.Application.Features.Columns;
using Zumbo.Modules.Boards.Application.Features.Lifecycle;
using Zumbo.Modules.Boards.Application.Features.Swimlanes;
using Zumbo.Modules.Boards.Application.Features.Views;
using Zumbo.SharedKernel;

namespace Zumbo.Api.Composition.Modules.Boards;

internal static class BoardModuleComposition
{
    internal static IServiceCollection AddBoardServices(this IServiceCollection services)
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
        services.AddScoped<UpdateBoardHandler>();
        services.AddScoped<ArchiveBoardHandler>();
        services.AddScoped<RestoreBoardHandler>();
        services.AddScoped<UpdateSwimlaneHandler>();
        services.AddScoped<AddColumnHandler>();
        services.AddScoped<UpdateColumnHandler>();
        services.AddScoped<DeleteColumnHandler>();
        services.AddScoped<ReorderColumnsHandler>();
        services.AddScoped<CreateViewHandler>();
        services.AddScoped<UpdateViewHandler>();
        services.AddScoped<DeleteViewHandler>();
        return services;
    }
}
