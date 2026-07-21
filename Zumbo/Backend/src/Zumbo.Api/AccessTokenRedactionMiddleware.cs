public sealed class AccessTokenRedactionMiddleware(RequestDelegate next)
{
    private const string AccessTokenParameter = "access_token";

    public async Task InvokeAsync(HttpContext context)
    {
        if (context.Request.Path.StartsWithSegments("/hubs/work-items")
            && context.Request.Query.TryGetValue(AccessTokenParameter, out var values))
        {
            if (values.Count != 1 || string.IsNullOrWhiteSpace(values[0]))
            {
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                return;
            }

            if (!context.Request.Headers.ContainsKey("Authorization"))
            {
                context.Request.Headers.Authorization = "Bearer " + values[0];
            }

            context.Request.QueryString = QueryString.Create(
                context.Request.Query
                    .Where(entry => !entry.Key.Equals(AccessTokenParameter, StringComparison.OrdinalIgnoreCase))
                    .SelectMany(entry => entry.Value, (entry, value) =>
                        new KeyValuePair<string, string?>(entry.Key, value)));
        }

        await next(context);
    }
}
