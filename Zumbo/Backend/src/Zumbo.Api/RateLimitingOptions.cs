public sealed class RateLimitingOptions
{
    public int LoginPermitLimit { get; init; } = 10;
    public int PasswordResetPermitLimit { get; init; } = 5;
    public int ApiPermitLimit { get; init; } = 300;
    public int SearchPermitLimit { get; init; } = 60;
    public int UploadPermitLimit { get; init; } = 10;
    public int RealtimeConnectPermitLimit { get; init; } = 500;
}
