using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Identity;

public sealed record RegisterUserRequest(
    string Username,
    string Email,
    string Password,
    string OrganizationId,
    string? BootstrapToken = null);
