namespace Zumbo.SharedKernel;

public sealed class AuthenticationChallengeException(string code, string message)
    : ZumboException(code, message);
