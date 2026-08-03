using System.Net;
using System.Net.Sockets;
using System.Text;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Runtime;
using Zumbo.Modules.WorkItems;
using Zumbo.Modules.Organizations;
using Zumbo.SharedKernel;

public sealed class WebhookAuthorizationAdapter(
    IDocumentRepository<OrganizationDocument> organizations,
    ICurrentUser currentUser) : IWebhookAuthorization
{
    public async Task EnsureCanManageAsync(string organizationId, CancellationToken ct)
    {
        var userId = currentUser.UserId
            ?? throw new UnauthorizedException("Authenticated user is required.");
        if (!string.Equals(currentUser.OrganizationId, organizationId, StringComparison.Ordinal))
            throw new ForbiddenException("Webhook management is restricted to the active tenant.");
        if (currentUser.Roles.Contains("SystemAdmin", StringComparer.OrdinalIgnoreCase)
            || currentUser.Roles.Contains("OrganizationAdmin", StringComparer.OrdinalIgnoreCase))
        {
            return;
        }

        var organization = await organizations.SelectAsync(
            x => x.Id == organizationId || x.TenantKey == organizationId,
            ct);
        if (organization is null
            || !string.Equals(organization.OwnerUserId, userId, StringComparison.Ordinal))
        {
            throw new ForbiddenException("Organization owner or administrator permission is required.");
        }
    }
}
