using Zumbo.BuildingBlocks.Application.Messaging;

public sealed class TeamTransactionFilter(
    IDurableTransactionRunner transactions) : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(
        EndpointFilterInvocationContext context,
        EndpointFilterDelegate next)
    {
        var method = context.HttpContext.Request.Method;
        if (HttpMethods.IsGet(method) || HttpMethods.IsHead(method) || HttpMethods.IsOptions(method))
        {
            return await next(context);
        }

        return await transactions.ExecuteAsync(
            "Teams",
            _ => next(context).AsTask(),
            context.HttpContext.RequestAborted);
    }
}
