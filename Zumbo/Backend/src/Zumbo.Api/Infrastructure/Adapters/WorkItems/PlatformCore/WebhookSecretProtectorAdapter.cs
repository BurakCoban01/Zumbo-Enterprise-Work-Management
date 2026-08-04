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

public sealed class WebhookSecretProtectorAdapter(IDataProtectionProvider provider) : IWebhookSecretProtector
{
    private readonly IDataProtector protector = provider.CreateProtector("Zumbo.WorkItems.WebhookSecret.v1");

    public string Protect(string value) => protector.Protect(value);
    public string Unprotect(string value) => protector.Unprotect(value);
}
