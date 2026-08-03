namespace Zumbo.SharedKernel;

public sealed class ForbiddenException(string message = "Permission denied.")
    : ZumboException("FORBIDDEN", message);
