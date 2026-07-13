namespace Zumbo.SharedKernel;

public abstract class ZumboException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}

public sealed class ValidationException(string message)
    : ZumboException("VALIDATION_ERROR", message);

public sealed class UnauthorizedException(string message = "Authentication failed.")
    : ZumboException("UNAUTHORIZED", message);

public sealed class AuthenticationChallengeException(string code, string message)
    : ZumboException(code, message);

public sealed class ForbiddenException(string message = "Permission denied.")
    : ZumboException("FORBIDDEN", message);

public sealed class NotFoundException(string code, string message)
    : ZumboException(code, message);

public sealed class ConflictException(string code, string message)
    : ZumboException(code, message);
