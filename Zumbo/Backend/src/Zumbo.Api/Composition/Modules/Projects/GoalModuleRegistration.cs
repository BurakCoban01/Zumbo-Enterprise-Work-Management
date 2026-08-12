using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.Modules.Projects;
using Zumbo.Modules.Projects.Application.Features.Goals;
using Zumbo.SharedKernel;

internal static class GoalModuleRegistration
{
    internal static IServiceCollection AddGoalModule(this IServiceCollection services)
    {
        services.AddScoped<IGoalDirectory, GoalDirectoryAdapter>();
        services.AddScoped<IGoalAuditWriter, GoalAuditWriterAdapter>();
        services.AddScoped<GoalService>();
        services.AddScoped<ListGoalsHandler>(provider => new ListGoalsHandler(
            provider.GetRequiredService<IDocumentRepository<GoalDocument>>(), provider.GetRequiredService<ICurrentUser>()));
        services.AddScoped<GetGoalHandler>(provider => new GetGoalHandler(
            provider.GetRequiredService<IDocumentRepository<GoalDocument>>(), provider.GetRequiredService<ICurrentUser>()));
        services.AddScoped<GetGoalRollupHandler>(provider => new GetGoalRollupHandler(
            provider.GetRequiredService<IDocumentRepository<GoalDocument>>(), provider.GetRequiredService<IGoalDirectory>(),
            provider.GetRequiredService<ICurrentUser>(), provider.GetRequiredService<IClock>()));
        services.AddScoped<AddKeyResultProgressHandler>(provider => new AddKeyResultProgressHandler(
            provider.GetRequiredService<IDocumentRepository<GoalDocument>>(), provider.GetRequiredService<IGoalAuditWriter>(),
            provider.GetRequiredService<ICurrentUser>(), provider.GetRequiredService<IClock>(), provider.GetService<IExpectedVersionAccessor>()));
        services.AddScoped<AddGoalStatusUpdateHandler>(provider => new AddGoalStatusUpdateHandler(
            provider.GetRequiredService<IDocumentRepository<GoalDocument>>(), provider.GetRequiredService<IGoalAuditWriter>(),
            provider.GetRequiredService<ICurrentUser>(), provider.GetRequiredService<IClock>(), provider.GetService<IExpectedVersionAccessor>()));
        services.AddScoped<SaveGoalHandler>(provider => new SaveGoalHandler(
            provider.GetRequiredService<IDocumentRepository<GoalDocument>>(), provider.GetRequiredService<IGoalDirectory>(),
            provider.GetRequiredService<IGoalAuditWriter>(), provider.GetRequiredService<ICurrentUser>(),
            provider.GetRequiredService<IClock>(), provider.GetService<IExpectedVersionAccessor>()));
        services.AddScoped<SaveKeyResultHandler>(provider => new SaveKeyResultHandler(
            provider.GetRequiredService<IDocumentRepository<GoalDocument>>(), provider.GetRequiredService<IGoalDirectory>(),
            provider.GetRequiredService<IGoalAuditWriter>(), provider.GetRequiredService<ICurrentUser>(),
            provider.GetRequiredService<IClock>(), provider.GetService<IExpectedVersionAccessor>()));
        services.AddScoped<ArchiveGoalHandler>(provider => new ArchiveGoalHandler(
            provider.GetRequiredService<IDocumentRepository<GoalDocument>>(), provider.GetRequiredService<IGoalAuditWriter>(),
            provider.GetRequiredService<ICurrentUser>(), provider.GetRequiredService<IClock>(), provider.GetService<IExpectedVersionAccessor>()));
        return services;
    }
}
