using Microsoft.AspNetCore.Mvc;
using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.Modules.Projects;
using Zumbo.Modules.Projects.Application.Features.Portfolio;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

using static ApiEndpointResults;

internal static class PortfolioEndpoints
{
    internal static IServiceCollection AddPortfolioModule(this IServiceCollection services)
    {
        services.AddScoped<IPortfolioDirectory, PortfolioDirectoryAdapter>();
        services.AddScoped<IPortfolioAuditWriter, PortfolioAuditWriterAdapter>();
        services.AddScoped<PortfolioService>();
        services.AddScoped<ListPortfoliosHandler>(provider => new ListPortfoliosHandler(
            provider.GetRequiredService<IDocumentRepository<PortfolioDocument>>(),
            provider.GetRequiredService<ICurrentUser>()));
        services.AddScoped<GetPortfolioHandler>(provider => new GetPortfolioHandler(
            provider.GetRequiredService<IDocumentRepository<PortfolioDocument>>(),
            provider.GetRequiredService<ICurrentUser>()));
        services.AddScoped<GetPortfolioRoadmapHandler>(provider => new GetPortfolioRoadmapHandler(
            provider.GetRequiredService<IDocumentRepository<PortfolioDocument>>(),
            provider.GetRequiredService<IPortfolioDirectory>(),
            provider.GetRequiredService<ICurrentUser>(),
            provider.GetRequiredService<IClock>()));
        services.AddScoped<SavePortfolioHandler>(provider => new SavePortfolioHandler(
            provider.GetRequiredService<IDocumentRepository<PortfolioDocument>>(),
            provider.GetRequiredService<IPortfolioDirectory>(),
            provider.GetRequiredService<IPortfolioAuditWriter>(),
            provider.GetRequiredService<ICurrentUser>(),
            provider.GetRequiredService<IClock>(),
            provider.GetService<IExpectedVersionAccessor>()));
        services.AddScoped<ArchivePortfolioHandler>(provider => new ArchivePortfolioHandler(
            provider.GetRequiredService<IDocumentRepository<PortfolioDocument>>(),
            provider.GetRequiredService<IPortfolioAuditWriter>(),
            provider.GetRequiredService<ICurrentUser>(),
            provider.GetRequiredService<IClock>(),
            provider.GetService<IExpectedVersionAccessor>()));
        services.AddScoped<SaveInitiativeHandler>(provider => new SaveInitiativeHandler(
            provider.GetRequiredService<IDocumentRepository<PortfolioDocument>>(),
            provider.GetRequiredService<IPortfolioDirectory>(),
            provider.GetRequiredService<IPortfolioAuditWriter>(),
            provider.GetRequiredService<ICurrentUser>(),
            provider.GetRequiredService<IClock>(),
            provider.GetService<IExpectedVersionAccessor>()));
        services.AddScoped<AddInitiativeStatusUpdateHandler>(provider =>
            new AddInitiativeStatusUpdateHandler(
                provider.GetRequiredService<IDocumentRepository<PortfolioDocument>>(),
                provider.GetRequiredService<IPortfolioAuditWriter>(),
                provider.GetRequiredService<ICurrentUser>(),
                provider.GetRequiredService<IClock>(),
                provider.GetService<IExpectedVersionAccessor>()));
        services.AddScoped<SavePortfolioDependencyHandler>(provider =>
            new SavePortfolioDependencyHandler(
                provider.GetRequiredService<IDocumentRepository<PortfolioDocument>>(),
                provider.GetRequiredService<IPortfolioDirectory>(),
                provider.GetRequiredService<IPortfolioAuditWriter>(),
                provider.GetRequiredService<ICurrentUser>(),
                provider.GetRequiredService<IClock>(),
                provider.GetService<IExpectedVersionAccessor>()));
        return services;
    }

    internal static void MapPortfolioEndpoints(this RouteGroupBuilder api)
    {
        var group = api.MapGroup("/portfolios")
            .WithTags("Portfolios")
            .RequireAuthorization()
            .WithZumboPermission(PermissionCatalog.ProjectView);

        group.MapGet("", async (
            bool? includeArchived,
            int? page,
            int? pageSize,
            [FromServices] ListPortfoliosHandler handler,
            HttpContext http,
            CancellationToken ct) =>
            Ok(await handler.HandleAsync(
                new ListPortfoliosQuery(
                    includeArchived ?? false,
                    page ?? 1,
                    pageSize ?? 50),
                ct), http));

        group.MapGet("/{portfolioId}", async (
            string portfolioId,
            bool? includeArchived,
            [FromServices] GetPortfolioHandler handler,
            HttpContext http,
            CancellationToken ct) =>
            Ok(await handler.HandleAsync(
                new GetPortfolioQuery(portfolioId, includeArchived ?? false), ct), http));

        group.MapPost("", async (
            SavePortfolioRequest request,
            [FromServices] SavePortfolioHandler handler,
            HttpContext http,
            CancellationToken ct) =>
            Ok(await handler.HandleAsync(
                new SavePortfolioCommand(null, request, CorrelationId(http)), ct), http));

        group.MapPut("/{portfolioId}", async (
            string portfolioId,
            SavePortfolioRequest request,
            [FromServices] SavePortfolioHandler handler,
            HttpContext http,
            CancellationToken ct) =>
            Ok(await handler.HandleAsync(
                new SavePortfolioCommand(portfolioId, request, CorrelationId(http)), ct), http));

        group.MapPost("/{portfolioId}/initiatives", async (
            string portfolioId,
            SaveInitiativeRequest request,
            [FromServices] SaveInitiativeHandler handler,
            HttpContext http,
            CancellationToken ct) =>
            Ok(await handler.HandleAsync(
                new SaveInitiativeCommand(
                    portfolioId, null, request, CorrelationId(http)), ct), http));

        group.MapPut("/{portfolioId}/initiatives/{initiativeId}", async (
            string portfolioId,
            string initiativeId,
            SaveInitiativeRequest request,
            [FromServices] SaveInitiativeHandler handler,
            HttpContext http,
            CancellationToken ct) =>
            Ok(await handler.HandleAsync(
                new SaveInitiativeCommand(
                    portfolioId, initiativeId, request, CorrelationId(http)), ct), http));

        group.MapPost("/{portfolioId}/initiatives/{initiativeId}/status-updates", async (
            string portfolioId,
            string initiativeId,
            AddInitiativeStatusUpdateRequest request,
            [FromServices] AddInitiativeStatusUpdateHandler handler,
            HttpContext http,
            CancellationToken ct) =>
            Ok(await handler.HandleAsync(
                new AddInitiativeStatusUpdateCommand(
                    portfolioId,
                    initiativeId,
                    request,
                    CorrelationId(http)),
                ct), http));

        group.MapPost("/{portfolioId}/dependencies", async (
            string portfolioId,
            SavePortfolioDependencyRequest request,
            [FromServices] SavePortfolioDependencyHandler handler,
            HttpContext http,
            CancellationToken ct) =>
            Ok(await handler.HandleAsync(
                new SavePortfolioDependencyCommand(
                    portfolioId, null, request, CorrelationId(http)), ct), http));

        group.MapPut("/{portfolioId}/dependencies/{dependencyId}", async (
            string portfolioId,
            string dependencyId,
            SavePortfolioDependencyRequest request,
            [FromServices] SavePortfolioDependencyHandler handler,
            HttpContext http,
            CancellationToken ct) =>
            Ok(await handler.HandleAsync(
                new SavePortfolioDependencyCommand(
                    portfolioId,
                    dependencyId,
                    request,
                    CorrelationId(http)),
                ct), http));

        group.MapGet("/{portfolioId}/roadmap", async (
            string portfolioId,
            [FromServices] GetPortfolioRoadmapHandler handler,
            HttpContext http,
            CancellationToken ct) =>
            Ok(await handler.HandleAsync(
                new GetPortfolioRoadmapQuery(portfolioId), ct), http));

        group.MapDelete("/{portfolioId}", async (
            string portfolioId,
            [FromServices] ArchivePortfolioHandler handler,
            HttpContext http,
            CancellationToken ct) =>
        {
            await handler.HandleAsync(
                new ArchivePortfolioCommand(portfolioId, CorrelationId(http)), ct);
            return Ok(new { archived = true }, http);
        });
    }
}
