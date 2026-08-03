namespace Zumbo.SharedKernel;

public sealed class NotFoundException(string code, string message)
    : ZumboException(code, message);
