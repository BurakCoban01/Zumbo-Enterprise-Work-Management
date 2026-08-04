using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.Modules.Projects;
using Zumbo.Modules.WorkItems;
using Zumbo.SharedKernel;

public sealed class DevelopmentIntegrationAuthorizationAdapter(
    IWebhookAuthorization authorization) : IDevelopmentIntegrationAuthorization
{
    public Task EnsureCanManageAsync(string organizationId, CancellationToken ct) =>
        authorization.EnsureCanManageAsync(organizationId, ct);
}
