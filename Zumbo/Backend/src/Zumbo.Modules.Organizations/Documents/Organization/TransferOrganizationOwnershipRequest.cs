using Zumbo.BuildingBlocks.Application.Persistence;

namespace Zumbo.Modules.Organizations;
public sealed record TransferOrganizationOwnershipRequest(string NewOwnerUserId);
