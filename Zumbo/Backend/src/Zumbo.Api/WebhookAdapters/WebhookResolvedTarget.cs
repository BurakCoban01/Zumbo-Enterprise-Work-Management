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

internal sealed record WebhookResolvedTarget(Uri Uri, IReadOnlyList<IPAddress> Addresses);
