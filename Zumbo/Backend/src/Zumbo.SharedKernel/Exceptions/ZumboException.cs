namespace Zumbo.SharedKernel;

public abstract class ZumboException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}
