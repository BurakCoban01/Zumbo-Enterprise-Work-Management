namespace Zumbo.SharedKernel;

public sealed record ApiResponse<T>(
    bool Success,
    T? Data,
    ApiError? Error,
    string CorrelationId)
{
    public static ApiResponse<T> Ok(T data, string correlationId) =>
        new(true, data, null, correlationId);

    public static ApiResponse<T> Fail(string code, string message, string correlationId) =>
        new(false, default, new ApiError(code, message), correlationId);
}
