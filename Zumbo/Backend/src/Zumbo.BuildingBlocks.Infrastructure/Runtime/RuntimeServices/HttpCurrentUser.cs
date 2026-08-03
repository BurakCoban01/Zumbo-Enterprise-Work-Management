using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Zumbo.SharedKernel;

namespace Zumbo.BuildingBlocks.Infrastructure.Runtime;

public sealed class HttpCurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    public string? UserId =>
        accessor.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier)
        ?? accessor.HttpContext?.User.FindFirstValue("sub");

    public string? OrganizationId =>
        accessor.HttpContext?.User.FindFirstValue("organizationId");

    public IReadOnlyCollection<string> Roles =>
        accessor.HttpContext?.User.FindAll(ClaimTypes.Role).Select(x => x.Value).ToArray()
        ?? [];
}
