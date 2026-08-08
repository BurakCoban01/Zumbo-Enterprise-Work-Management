using Zumbo.Modules.Workflows;
using Zumbo.Modules.WorkItems;

namespace Zumbo.Api.Composition.Modules.WorkItems;

internal static class WorkItemAutomationComposition
{
    internal static IServiceCollection AddWorkItemAutomationActionAdapter(this IServiceCollection services)
    {
        services.AddScoped<IAutomationActionExecutor>(provider => new AutomationWorkItemActionExecutor(
            provider.GetRequiredService<GetWorkItemHandler>(),
            provider.GetRequiredService<AssignWorkItemHandler>(),
            provider.GetRequiredService<ClearAssigneeHandler>(),
            provider.GetRequiredService<AddLabelHandler>(),
            provider.GetRequiredService<RemoveLabelHandler>(),
            provider.GetRequiredService<UpdateWorkItemHandler>(),
            provider.GetRequiredService<AddCommentHandler>(),
            provider.GetRequiredService<IWorkItemAutomationChainContextAccessor>()));
        return services;
    }
}
