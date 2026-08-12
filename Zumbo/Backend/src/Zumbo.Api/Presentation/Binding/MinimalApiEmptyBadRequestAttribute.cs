using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Zumbo.Api.Presentation.Binding;

[AttributeUsage(AttributeTargets.Method)]
public sealed class MinimalApiEmptyBadRequestAttribute : Attribute,
    IResourceFilter,
    IActionFilter,
    IOrderedFilter
{
    public int Order => -3000;

    public void OnResourceExecuting(ResourceExecutingContext context)
    {
        var request = context.HttpContext.Request;
        if ((request.ContentLength ?? 0) == 0 && request.Headers.TransferEncoding.Count == 0)
        {
            context.Result = EmptyBadRequest(context.HttpContext);
        }
    }

    public void OnResourceExecuted(ResourceExecutedContext context)
    {
    }

    public void OnActionExecuting(ActionExecutingContext context)
    {
        if (!context.ModelState.IsValid)
        {
            context.Result = EmptyBadRequest(context.HttpContext);
        }
    }

    public void OnActionExecuted(ActionExecutedContext context)
    {
    }

    private static IActionResult EmptyBadRequest(HttpContext httpContext)
    {
        httpContext.Response.StatusCode = StatusCodes.Status400BadRequest;
        return new EmptyResult();
    }
}
