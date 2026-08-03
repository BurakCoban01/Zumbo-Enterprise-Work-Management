namespace Zumbo.SharedKernel;

public sealed class ConflictException(string code, string message)
    : ZumboException(code, message);
