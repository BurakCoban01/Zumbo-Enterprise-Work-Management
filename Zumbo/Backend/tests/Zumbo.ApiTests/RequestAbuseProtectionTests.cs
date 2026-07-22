using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace Zumbo.ApiTests;

public sealed class RequestAbuseProtectionTests
{
    [Fact]
    public Task OversizedBody_IsRejectedBeforeEndpoint() =>
        AssertRejectedAsync(
            context => context.Request.ContentLength = 27 * 1024 * 1024,
            StatusCodes.Status413PayloadTooLarge);

    [Fact]
    public Task ExcessiveHeaders_AreRejectedBeforeEndpoint() =>
        AssertRejectedAsync(
            context =>
            {
                for (var index = 0; index < 101; index++)
                {
                    context.Request.Headers[$"X-Test-{index}"] = "value";
                }
            },
            StatusCodes.Status431RequestHeaderFieldsTooLarge);

    [Fact]
    public Task OversizedQueryValue_IsRejectedBeforeEndpoint() =>
        AssertRejectedAsync(
            context => context.Request.QueryString = new QueryString("?search=" + new string('a', 2049)),
            StatusCodes.Status414UriTooLong);

    [Theory]
    [InlineData("?page=0")]
    [InlineData("?page=10001")]
    [InlineData("?pageSize=0")]
    [InlineData("?pageSize=201")]
    [InlineData("?pageSize=not-a-number")]
    public Task InvalidPagination_IsRejectedBeforeEndpoint(string query) =>
        AssertRejectedAsync(
            context => context.Request.QueryString = new QueryString(query),
            StatusCodes.Status400BadRequest);

    [Fact]
    public async Task BoundedRequest_Continues()
    {
        var nextCalled = false;
        var middleware = new RequestAbuseProtectionMiddleware(
            _ =>
            {
                nextCalled = true;
                return Task.CompletedTask;
            },
            Options.Create(new RequestLimitsOptions()));
        var context = Context();
        context.Request.QueryString = new QueryString("?page=1&pageSize=200");

        await middleware.InvokeAsync(context);

        Assert.True(nextCalled);
    }

    private static async Task AssertRejectedAsync(Action<DefaultHttpContext> arrange, int expectedStatus)
    {
        var nextCalled = false;
        var middleware = new RequestAbuseProtectionMiddleware(
            _ =>
            {
                nextCalled = true;
                return Task.CompletedTask;
            },
            Options.Create(new RequestLimitsOptions()));
        var context = Context();
        arrange(context);

        await middleware.InvokeAsync(context);

        Assert.False(nextCalled);
        Assert.Equal(expectedStatus, context.Response.StatusCode);
        context.Response.Body.Position = 0;
        using var payload = await JsonDocument.ParseAsync(context.Response.Body);
        Assert.Equal("REQUEST_LIMIT_EXCEEDED", payload.RootElement.GetProperty("error").GetProperty("code").GetString());
    }

    private static DefaultHttpContext Context()
    {
        var context = new DefaultHttpContext { TraceIdentifier = "request-limit-correlation" };
        context.Response.Body = new MemoryStream();
        return context;
    }
}
