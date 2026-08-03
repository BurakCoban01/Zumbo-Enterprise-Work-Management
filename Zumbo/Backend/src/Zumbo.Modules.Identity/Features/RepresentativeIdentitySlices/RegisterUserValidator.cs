using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Identity;

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
