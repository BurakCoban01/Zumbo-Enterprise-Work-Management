namespace Zumbo.Modules.Identity;

public sealed class SearchUsersValidator
{
    public static void Validate(SearchUsersQuery query) => ArgumentNullException.ThrowIfNull(query);
}
