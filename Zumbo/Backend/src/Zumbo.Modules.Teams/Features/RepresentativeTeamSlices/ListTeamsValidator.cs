using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Teams;

public sealed class ListTeamsValidator
{
    public static void Validate(ListTeamsQuery query) => ArgumentNullException.ThrowIfNull(query);
}
