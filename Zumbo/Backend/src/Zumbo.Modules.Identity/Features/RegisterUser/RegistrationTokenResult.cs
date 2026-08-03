namespace Zumbo.Modules.Identity;

internal sealed record RegistrationTokenResult(
    AuthResponse Response,
    RefreshSessionDocument Session);
