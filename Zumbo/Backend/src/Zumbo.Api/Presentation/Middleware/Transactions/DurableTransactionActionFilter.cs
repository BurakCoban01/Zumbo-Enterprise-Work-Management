using Microsoft.AspNetCore.Mvc.Filters;
using Zumbo.BuildingBlocks.Application.Messaging;

namespace Zumbo.Api.Presentation.Middleware.Transactions;

public sealed class DurableTransactionActionFilter(
    IDurableTransactionRunner transactions,
    string ownerModule,
    string? bypassPathPrefix = null) : IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var request = context.HttpContext.Request;
        if (HttpMethods.IsGet(request.Method)
            || HttpMethods.IsHead(request.Method)
            || HttpMethods.IsOptions(request.Method)
            || (!string.IsNullOrWhiteSpace(bypassPathPrefix)
                && request.Path.StartsWithSegments(bypassPathPrefix, StringComparison.Ordinal)))
        {
            await next();
            return;
        }

        await transactions.ExecuteAsync(
            ownerModule,
            async _ => { await next(); },
            context.HttpContext.RequestAborted);
    }
}
