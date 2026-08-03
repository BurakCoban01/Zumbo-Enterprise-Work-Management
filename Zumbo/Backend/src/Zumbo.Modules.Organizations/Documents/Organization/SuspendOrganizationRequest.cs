using Zumbo.BuildingBlocks.Application.Persistence;

namespace Zumbo.Modules.Organizations;
public sealed record SuspendOrganizationRequest(string? Reason = null);
