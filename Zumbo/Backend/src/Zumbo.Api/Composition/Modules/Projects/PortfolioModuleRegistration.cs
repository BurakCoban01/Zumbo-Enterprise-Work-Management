using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.Modules.Projects;
using Zumbo.Modules.Projects.Application.Features.Portfolio;
using Zumbo.SharedKernel;

internal static class PortfolioModuleRegistration
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
        services.AddScoped<AddInitiativeStatusUpdateHandler>(provider => new AddInitiativeStatusUpdateHandler(
            provider.GetRequiredService<IDocumentRepository<PortfolioDocument>>(),
            provider.GetRequiredService<IPortfolioAuditWriter>(),
            provider.GetRequiredService<ICurrentUser>(),
            provider.GetRequiredService<IClock>(),
            provider.GetService<IExpectedVersionAccessor>()));
        services.AddScoped<SavePortfolioDependencyHandler>(provider => new SavePortfolioDependencyHandler(
            provider.GetRequiredService<IDocumentRepository<PortfolioDocument>>(),
            provider.GetRequiredService<IPortfolioDirectory>(),
            provider.GetRequiredService<IPortfolioAuditWriter>(),
            provider.GetRequiredService<ICurrentUser>(),
            provider.GetRequiredService<IClock>(),
            provider.GetService<IExpectedVersionAccessor>()));
        return services;
    }
}
