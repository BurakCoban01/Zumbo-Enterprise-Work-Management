namespace Zumbo.Modules.WorkItems;

public interface IDevelopmentCredentialProtector
{
    string Protect(string value);
    string Unprotect(string value);
}
