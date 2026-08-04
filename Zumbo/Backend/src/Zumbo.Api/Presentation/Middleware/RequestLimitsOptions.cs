using System.Text;
using Microsoft.Extensions.Options;
using Zumbo.SharedKernel;

public sealed class RequestLimitsOptions
{
    public long MaxRequestBodyBytes { get; init; } = 26 * 1024 * 1024;
    public int MaxHeaderCount { get; init; } = 100;
    public int MaxHeaderBytes { get; init; } = 32 * 1024;
    public int MaxQueryStringBytes { get; init; } = 8 * 1024;
    public int MaxQueryParameters { get; init; } = 50;
    public int MaxQueryValueCharacters { get; init; } = 2048;
    public int MaxPage { get; init; } = 10_000;
    public int MaxPageSize { get; init; } = 200;
}
