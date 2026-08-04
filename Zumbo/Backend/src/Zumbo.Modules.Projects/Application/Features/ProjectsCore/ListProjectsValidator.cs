namespace Zumbo.Modules.Projects;

public sealed class ListProjectsValidator
{
    public static void Validate(ListProjectsQuery query) => ArgumentNullException.ThrowIfNull(query);
}
