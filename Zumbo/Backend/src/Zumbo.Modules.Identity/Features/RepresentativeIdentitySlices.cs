using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Identity;

public sealed record RegisterUserRequest(
    string Username,
    string Email,
    string Password,
    string OrganizationId,
    string? BootstrapToken = null);

public sealed record AuthResponse(
    string AccessToken,
    string RefreshToken,
    DateTimeOffset ExpiresAt,
    UserProfileResponse User);

public sealed record UserProfileResponse(
    string Id,
    string Username,
    string Email,
    string OrganizationId,
    IReadOnlyCollection<string> Roles,
    long Version = 0) : IVersionedResource;

public sealed class RegisterUserValidator
{
    public static void Validate(RegisterUserRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Username) || request.Username.Length < 3)
        {
            throw new ValidationException("Username must be at least 3 characters.");
        }

        if (string.IsNullOrWhiteSpace(request.Email) || !request.Email.Contains('@'))
        {
            throw new ValidationException("Valid email is required.");
        }

        if (string.IsNullOrWhiteSpace(request.OrganizationId))
        {
            throw new ValidationException("Organization id is required.");
        }

        if (string.IsNullOrWhiteSpace(request.Password)
            || request.Password.Length < 10
            || !request.Password.Any(char.IsUpper)
            || !request.Password.Any(char.IsLower)
            || !request.Password.Any(char.IsDigit)
            || request.Password.All(char.IsLetterOrDigit))
        {
            throw new ValidationException("Password must be at least 10 characters and include upper-case, lower-case, number and symbol characters.");
        }
    }
}

public sealed class RegisterUserHandler(IdentityService service)
{
    public Task<AuthResponse> HandleAsync(RegisterUserRequest request, CancellationToken ct) =>
        service.RegisterAsync(request, ct);
}

public sealed record SearchUsersQuery(string? Search);

public sealed class SearchUsersValidator
{
    public static void Validate(SearchUsersQuery query) => ArgumentNullException.ThrowIfNull(query);
}

public sealed class SearchUsersHandler(IdentityService service)
{
    public Task<IReadOnlyList<UserProfileResponse>> HandleAsync(SearchUsersQuery query, CancellationToken ct)
    {
        SearchUsersValidator.Validate(query);
        return service.SearchUsersAsync(query.Search, ct);
    }
}
