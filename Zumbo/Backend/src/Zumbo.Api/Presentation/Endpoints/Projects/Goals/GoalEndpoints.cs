using Microsoft.AspNetCore.Mvc;
using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.Modules.Projects;
using Zumbo.Modules.Projects.Application.Features.Goals;
using Zumbo.SharedKernel;

using static ApiEndpointResults;

internal static class GoalEndpoints
{
    internal static IServiceCollection AddGoalModule(this IServiceCollection services)
    {
        services.AddScoped<IGoalDirectory, GoalDirectoryAdapter>();
        services.AddScoped<IGoalAuditWriter, GoalAuditWriterAdapter>();
        services.AddScoped<GoalService>();
        services.AddScoped<ListGoalsHandler>(provider => new ListGoalsHandler(
            provider.GetRequiredService<IDocumentRepository<GoalDocument>>(),
            provider.GetRequiredService<ICurrentUser>()));
        services.AddScoped<GetGoalHandler>(provider => new GetGoalHandler(
            provider.GetRequiredService<IDocumentRepository<GoalDocument>>(),
            provider.GetRequiredService<ICurrentUser>()));
        services.AddScoped<GetGoalRollupHandler>(provider => new GetGoalRollupHandler(
            provider.GetRequiredService<IDocumentRepository<GoalDocument>>(),
            provider.GetRequiredService<IGoalDirectory>(),
            provider.GetRequiredService<ICurrentUser>(),
            provider.GetRequiredService<IClock>()));
        services.AddScoped<AddKeyResultProgressHandler>(provider => new AddKeyResultProgressHandler(
            provider.GetRequiredService<IDocumentRepository<GoalDocument>>(),
            provider.GetRequiredService<IGoalAuditWriter>(),
            provider.GetRequiredService<ICurrentUser>(),
            provider.GetRequiredService<IClock>(),
            provider.GetService<IExpectedVersionAccessor>()));
        services.AddScoped<AddGoalStatusUpdateHandler>(provider => new AddGoalStatusUpdateHandler(
            provider.GetRequiredService<IDocumentRepository<GoalDocument>>(),
            provider.GetRequiredService<IGoalAuditWriter>(),
            provider.GetRequiredService<ICurrentUser>(),
            provider.GetRequiredService<IClock>(),
            provider.GetService<IExpectedVersionAccessor>()));
        services.AddScoped<SaveGoalHandler>(provider => new SaveGoalHandler(
            provider.GetRequiredService<IDocumentRepository<GoalDocument>>(),
            provider.GetRequiredService<IGoalDirectory>(),
            provider.GetRequiredService<IGoalAuditWriter>(),
            provider.GetRequiredService<ICurrentUser>(),
            provider.GetRequiredService<IClock>(),
            provider.GetService<IExpectedVersionAccessor>()));
        services.AddScoped<SaveKeyResultHandler>(provider => new SaveKeyResultHandler(
            provider.GetRequiredService<IDocumentRepository<GoalDocument>>(),
            provider.GetRequiredService<IGoalDirectory>(),
            provider.GetRequiredService<IGoalAuditWriter>(),
            provider.GetRequiredService<ICurrentUser>(),
            provider.GetRequiredService<IClock>(),
            provider.GetService<IExpectedVersionAccessor>()));
        services.AddScoped<ArchiveGoalHandler>(provider => new ArchiveGoalHandler(
            provider.GetRequiredService<IDocumentRepository<GoalDocument>>(),
            provider.GetRequiredService<IGoalAuditWriter>(),
            provider.GetRequiredService<ICurrentUser>(),
            provider.GetRequiredService<IClock>(),
            provider.GetService<IExpectedVersionAccessor>()));
        return services;
    }

    internal static void MapGoalEndpoints(this RouteGroupBuilder api)
    {
        var group = api.MapGroup("/goals")
            .WithTags("Goals")
            .RequireAuthorization()
            .WithZumboPermission(PermissionCatalog.ProjectView);

        group.MapGet("", async (
            bool? includeArchived,
            int? page,
            int? pageSize,
            [FromServices] ListGoalsHandler handler,
            HttpContext http,
            CancellationToken ct) =>
            Ok(await handler.HandleAsync(
                new ListGoalsQuery(
                    includeArchived ?? false,
                    page ?? 1,
                    pageSize ?? 50),
                ct), http));

        group.MapGet("/{goalId}", async (
            string goalId,
            bool? includeArchived,
            [FromServices] GetGoalHandler handler,
            HttpContext http,
            CancellationToken ct) =>
            Ok(await handler.HandleAsync(
                new GetGoalQuery(goalId, includeArchived ?? false), ct), http));

        group.MapGet("/{goalId}/rollup", async (
            string goalId,
            [FromServices] GetGoalRollupHandler handler,
            HttpContext http,
            CancellationToken ct) =>
            Ok(await handler.HandleAsync(new GetGoalRollupQuery(goalId), ct), http));

        group.MapPost("", async (
            SaveGoalRequest request,
            [FromServices] SaveGoalHandler handler,
            HttpContext http,
            CancellationToken ct) =>
            Ok(await handler.HandleAsync(
                new SaveGoalCommand(null, request, CorrelationId(http)), ct), http));

        group.MapPut("/{goalId}", async (
            string goalId,
            SaveGoalRequest request,
            [FromServices] SaveGoalHandler handler,
            HttpContext http,
            CancellationToken ct) =>
            Ok(await handler.HandleAsync(
                new SaveGoalCommand(goalId, request, CorrelationId(http)), ct), http));

        group.MapPost("/{goalId}/key-results", async (
            string goalId,
            SaveKeyResultRequest request,
            [FromServices] SaveKeyResultHandler handler,
            HttpContext http,
            CancellationToken ct) =>
            Ok(await handler.HandleAsync(
                new SaveKeyResultCommand(goalId, null, request, CorrelationId(http)),
                ct), http));

        group.MapPut("/{goalId}/key-results/{keyResultId}", async (
            string goalId,
            string keyResultId,
            SaveKeyResultRequest request,
            [FromServices] SaveKeyResultHandler handler,
            HttpContext http,
            CancellationToken ct) =>
            Ok(await handler.HandleAsync(
                new SaveKeyResultCommand(
                    goalId, keyResultId, request, CorrelationId(http)),
                ct), http));

        group.MapPost("/{goalId}/key-results/{keyResultId}/progress-updates", async (
            string goalId,
            string keyResultId,
            AddKeyResultProgressRequest request,
            [FromServices] AddKeyResultProgressHandler handler,
            HttpContext http,
            CancellationToken ct) =>
            Ok(await handler.HandleAsync(
                new AddKeyResultProgressCommand(
                    goalId, keyResultId, request, CorrelationId(http)),
                ct), http));

        group.MapPost("/{goalId}/status-updates", async (
            string goalId,
            AddGoalStatusUpdateRequest request,
            [FromServices] AddGoalStatusUpdateHandler handler,
            HttpContext http,
            CancellationToken ct) =>
            Ok(await handler.HandleAsync(
                new AddGoalStatusUpdateCommand(goalId, request, CorrelationId(http)),
                ct), http));

        group.MapDelete("/{goalId}", async (
            string goalId,
            [FromServices] ArchiveGoalHandler handler,
            HttpContext http,
            CancellationToken ct) =>
        {
            await handler.HandleAsync(
                new ArchiveGoalCommand(goalId, CorrelationId(http)), ct);
            return Ok(new { archived = true }, http);
        });
    }
}
