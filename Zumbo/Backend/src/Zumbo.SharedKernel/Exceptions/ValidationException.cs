namespace Zumbo.SharedKernel;

public sealed class ValidationException(string message)
    : ZumboException("VALIDATION_ERROR", message);
