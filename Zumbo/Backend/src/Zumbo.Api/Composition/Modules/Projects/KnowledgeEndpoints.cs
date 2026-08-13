using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.Modules.Projects;
using Zumbo.Modules.Projects.Application.Features.Knowledge;
using Zumbo.SharedKernel;

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

}
