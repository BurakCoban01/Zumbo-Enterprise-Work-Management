using Zumbo.BuildingBlocks.Application.Messaging;

public sealed class WorkItemTransactionFilter(
    IDurableTransactionRunner transactions) : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(
        EndpointFilterInvocationContext context,
        EndpointFilterDelegate next)
    {
        var method = context.HttpContext.Request.Method;
        if (HttpMethods.IsGet(method)
            || HttpMethods.IsHead(method)
            || HttpMethods.IsOptions(method)
            || context.HttpContext.Request.Path.StartsWithSegments(
                "/api/work-items/durable-messaging",
                StringComparison.Ordinal))
        {
            return await next(context);
        }

        return await transactions.ExecuteAsync(
            "WorkItems",
            _ => next(context).AsTask(),
            context.HttpContext.RequestAborted);
    }
}
