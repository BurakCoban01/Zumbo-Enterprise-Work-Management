namespace Zumbo.Modules.WorkItems;

public sealed record RotateDevelopmentCredentialRequest(
    string AccessToken,
    long ExpectedVersion);
