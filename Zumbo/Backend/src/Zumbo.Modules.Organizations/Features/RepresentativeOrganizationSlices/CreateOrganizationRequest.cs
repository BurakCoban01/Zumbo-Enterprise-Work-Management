using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Organizations;

public sealed record CreateOrganizationRequest(string Name, string TenantKey);
