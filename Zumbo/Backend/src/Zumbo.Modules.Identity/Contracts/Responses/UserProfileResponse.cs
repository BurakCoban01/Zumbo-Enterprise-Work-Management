using Zumbo.BuildingBlocks.Application.Persistence;

namespace Zumbo.Modules.Identity;

public sealed record UserProfileResponse(
    string Id,
    string Username,
    string Email,
    string OrganizationId,
    IReadOnlyCollection<string> Roles,
    long Version = 0) : IVersionedResource;
