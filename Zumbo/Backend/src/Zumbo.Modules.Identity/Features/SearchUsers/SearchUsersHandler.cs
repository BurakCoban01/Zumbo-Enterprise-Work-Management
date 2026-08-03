using Zumbo.SharedKernel;

namespace Zumbo.Modules.Identity;

public sealed class SearchUsersHandler(IdentityService service)
{
    private SearchUsersSlice? slice;

    public SearchUsersHandler(IUserRepository users, ICurrentUser currentUser)
        : this(null!)
    {
        slice = new SearchUsersSlice(users, currentUser);
    }

    public Task<IReadOnlyList<UserProfileResponse>> HandleAsync(SearchUsersQuery query, CancellationToken ct) =>
        slice?.HandleAsync(query, ct) ?? service.SearchUsersAsync(query.Search, ct);
}
