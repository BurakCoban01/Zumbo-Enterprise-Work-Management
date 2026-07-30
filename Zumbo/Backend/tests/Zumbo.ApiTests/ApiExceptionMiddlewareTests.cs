using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Zumbo.BuildingBlocks.Application.Persistence;

namespace Zumbo.ApiTests;

public sealed class ApiExceptionMiddlewareTests
{
    [Fact]
    public Task DocumentConflict_IsReturnedAsHttpConflict() =>
        AssertMappingAsync(
            new DocumentConflictException("duplicate document"),
            StatusCodes.Status409Conflict,
            "DOCUMENT_CONFLICT");

    [Fact]
    public Task InvalidDocumentQuery_IsReturnedAsBadRequest() =>
        AssertMappingAsync(
            new DocumentQueryException("invalid field"),
            StatusCodes.Status400BadRequest,
            "DOCUMENT_QUERY_INVALID");

    [Fact]
    public Task StaleDocumentVersion_IsReturnedAsConcurrencyConflict() =>
        AssertMappingAsync(
            new DocumentConcurrencyException("document-id", 2, 3),
            StatusCodes.Status409Conflict,
            "CONCURRENCY_CONFLICT");

    [Fact]
    public async Task ClientCanceledRequest_IsNotMappedToServerError()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var middleware = new ApiExceptionMiddleware(
            _ => Task.FromCanceled(cancellation.Token),
            NullLogger<ApiExceptionMiddleware>.Instance,
            new TestHostEnvironment());
        var context = new DefaultHttpContext
        {
            TraceIdentifier = "canceled-correlation",
            RequestAborted = cancellation.Token
        };
        context.Response.Body = new MemoryStream();

        await middleware.InvokeAsync(context);

        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
        Assert.Equal(0, context.Response.Body.Length);
        Assert.Equal(
            "canceled-correlation",
            context.Response.Headers["X-Correlation-Id"]);
    }

    private static async Task AssertMappingAsync(Exception exception, int statusCode, string code)
    {
        var middleware = new ApiExceptionMiddleware(
            _ => Task.FromException(exception),
            NullLogger<ApiExceptionMiddleware>.Instance,
            new TestHostEnvironment());
        var context = new DefaultHttpContext
        {
            TraceIdentifier = "data001-correlation"
        };
        context.Response.Body = new MemoryStream();

        await middleware.InvokeAsync(context);

        Assert.Equal(statusCode, context.Response.StatusCode);
        Assert.Equal("data001-correlation", context.Response.Headers["X-Correlation-Id"]);
        context.Response.Body.Position = 0;
        using var payload = await JsonDocument.ParseAsync(context.Response.Body);
        Assert.Equal(code, payload.RootElement.GetProperty("error").GetProperty("code").GetString());
        Assert.Equal(
            "data001-correlation",
            payload.RootElement.GetProperty("correlationId").GetString());
    }

    private sealed class TestHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Production;
        public string ApplicationName { get; set; } = "Zumbo.ApiTests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
