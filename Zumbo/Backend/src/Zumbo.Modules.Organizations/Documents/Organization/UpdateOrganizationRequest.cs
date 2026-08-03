using Zumbo.BuildingBlocks.Application.Persistence;

namespace Zumbo.Modules.Organizations;

public sealed record UpdateOrganizationRequest(string Name, string? TenantKey = null);
