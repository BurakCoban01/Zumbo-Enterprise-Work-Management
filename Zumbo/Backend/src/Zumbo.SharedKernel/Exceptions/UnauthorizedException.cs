namespace Zumbo.SharedKernel;

public sealed class UnauthorizedException(string message = "Authentication failed.")
    : ZumboException("UNAUTHORIZED", message);
