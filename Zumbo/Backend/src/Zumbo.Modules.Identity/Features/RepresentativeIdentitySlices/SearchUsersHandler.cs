using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Identity;

public sealed class SearchUsersHandler(IdentityService service)
{
    public Task<IReadOnlyList<UserProfileResponse>> HandleAsync(SearchUsersQuery query, CancellationToken ct)
    {
        SearchUsersValidator.Validate(query);
        return service.SearchUsersAsync(query.Search, ct);
    }
}
