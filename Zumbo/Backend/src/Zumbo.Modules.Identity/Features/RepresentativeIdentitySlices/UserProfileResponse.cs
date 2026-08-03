using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Identity;

public sealed record UserProfileResponse(
    string Id,
    string Username,
    string Email,
    string OrganizationId,
    IReadOnlyCollection<string> Roles,
    long Version = 0) : IVersionedResource;
