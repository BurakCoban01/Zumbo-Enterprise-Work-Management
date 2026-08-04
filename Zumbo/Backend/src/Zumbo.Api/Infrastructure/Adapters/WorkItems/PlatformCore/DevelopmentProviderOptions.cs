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

public sealed class DevelopmentProviderOptions
{
    public bool AllowHttpLoopback { get; set; }
    public bool AllowPrivateNetworkHosts { get; set; }
    public int RequestTimeoutSeconds { get; set; } = 10;
    public int MaximumResponseBytes { get; set; } = 2_097_152;
    public string[] AllowedHosts { get; set; } = ["api.github.com", "gitlab.com"];
}
