namespace Zumbo.Modules.Organizations;

public sealed class ListOrganizationsValidator
{
    public static void Validate(ListOrganizationsQuery query) => ArgumentNullException.ThrowIfNull(query);
}
