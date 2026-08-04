using Zumbo.BuildingBlocks.Application.Search;

namespace Zumbo.Modules.WorkItems;

public sealed class WorkItemAutomationChainContextAccessor : IWorkItemAutomationChainContextAccessor
{
    private WorkItemAutomationChainContext? current;

    public WorkItemAutomationChainContext? Current => current;

    public IDisposable Push(WorkItemAutomationChainContext context)
    {
        var previous = current;
        current = context;
        return new RestoreScope(() => current = previous);
    }

    private sealed class RestoreScope(Action restore) : IDisposable
    {
        private bool disposed;

        public void Dispose()
        {
            if (disposed)
                return;
            disposed = true;
            restore();
        }
    }
}
