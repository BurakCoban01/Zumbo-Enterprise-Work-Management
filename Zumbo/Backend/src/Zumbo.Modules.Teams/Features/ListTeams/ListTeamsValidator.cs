namespace Zumbo.Modules.Teams;

public sealed class ListTeamsValidator
{
    public static void Validate(ListTeamsQuery query) => ArgumentNullException.ThrowIfNull(query);
}
