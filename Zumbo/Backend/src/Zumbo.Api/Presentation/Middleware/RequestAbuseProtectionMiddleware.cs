using System.Text;
using Microsoft.Extensions.Options;
using Zumbo.SharedKernel;

internal sealed class RequestAbuseProtectionMiddleware(
    RequestDelegate next,
    IOptions<RequestLimitsOptions> options)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var limits = options.Value;
        if (context.Request.ContentLength > limits.MaxRequestBodyBytes)
        {
            await RejectAsync(context, StatusCodes.Status413PayloadTooLarge, "Request body exceeds the allowed size.");
            return;
        }

        if (context.Request.Headers.Count > limits.MaxHeaderCount
            || HeaderBytes(context.Request.Headers) > limits.MaxHeaderBytes)
        {
            await RejectAsync(context, StatusCodes.Status431RequestHeaderFieldsTooLarge, "Request headers exceed the allowed size.");
            return;
        }

        var rawQuery = context.Request.QueryString.Value ?? string.Empty;
        if (Encoding.UTF8.GetByteCount(rawQuery) > limits.MaxQueryStringBytes)
        {
            await RejectAsync(context, StatusCodes.Status414UriTooLong, "Request query exceeds the allowed size.");
            return;
        }

        var query = context.Request.Query;
        if (query.Count > limits.MaxQueryParameters
            || query.Any(entry => entry.Value.Any(value => value?.Length > limits.MaxQueryValueCharacters)))
        {
            await RejectAsync(context, StatusCodes.Status414UriTooLong, "Request query exceeds the allowed complexity.");
            return;
        }

        if (!ValidBoundedInteger(query, "page", 1, limits.MaxPage)
            || !ValidBoundedInteger(query, "pageSize", 1, limits.MaxPageSize))
        {
            await RejectAsync(context, StatusCodes.Status400BadRequest, "Pagination values are outside the allowed range.");
            return;
        }

        await next(context);
    }

    private static bool ValidBoundedInteger(
        IQueryCollection query,
        string name,
        int minimum,
        int maximum)
    {
        if (!query.TryGetValue(name, out var values))
        {
            return true;
        }

        return values.Count == 1
            && int.TryParse(values[0], out var value)
            && value >= minimum
            && value <= maximum;
    }

    private static long HeaderBytes(IHeaderDictionary headers) => headers.Sum(header =>
        (long)Encoding.UTF8.GetByteCount(header.Key)
        + header.Value.Sum(value => Encoding.UTF8.GetByteCount(value ?? string.Empty)));

    private static async Task RejectAsync(HttpContext context, int statusCode, string message)
    {
        context.Response.StatusCode = statusCode;
        context.Response.Headers["X-Correlation-Id"] = context.TraceIdentifier;
        await context.Response.WriteAsJsonAsync(
            ApiResponse<object>.Fail("REQUEST_LIMIT_EXCEEDED", message, context.TraceIdentifier),
            context.RequestAborted);
    }
}
