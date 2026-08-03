using Zumbo.BuildingBlocks.Application.Search;

namespace Zumbo.Modules.WorkItems;

public interface IWorkItemAutomationChainContextAccessor
{
    WorkItemAutomationChainContext? Current { get; }
    IDisposable Push(WorkItemAutomationChainContext context);
}
