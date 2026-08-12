using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Messaging;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Identity;
public sealed record RoleResponse(
    string Id,
    string Name,
    string DisplayName,
    string Description,
    string Scope,
    string? OrganizationId,
    bool IsSystem,
    bool IsActive,
    bool IsProtected,
    bool IsDefault,
    int DisplayOrder,
    IReadOnlyCollection<string> Permissions,
    long Version);
