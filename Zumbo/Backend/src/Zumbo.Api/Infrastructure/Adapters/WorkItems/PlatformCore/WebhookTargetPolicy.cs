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

public sealed class WebhookTargetPolicy(IOptions<WebhookOptions> options) : IWebhookTargetPolicy
{
    public async Task ValidateAsync(string targetUrl, CancellationToken ct)
    {
        _ = await ResolveAsync(targetUrl, ct);
    }

    internal async Task<WebhookResolvedTarget> ResolveAsync(string targetUrl, CancellationToken ct)
    {
        if (!Uri.TryCreate(targetUrl, UriKind.Absolute, out var uri)
            || !string.IsNullOrEmpty(uri.UserInfo)
            || !string.IsNullOrEmpty(uri.Fragment)
            || string.IsNullOrWhiteSpace(uri.Host))
        {
            throw new ValidationException("Webhook target URL is not valid.");
        }

        var allowLoopback = options.Value.AllowHttpLoopback;
        var isHttps = uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase);
        var isHttp = uri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase);
        if (!isHttps && !(allowLoopback && isHttp))
        {
            throw new ValidationException("Webhook target URL must use HTTPS.");
        }

        IPAddress[] addresses;
        try
        {
            addresses = await Dns.GetHostAddressesAsync(uri.DnsSafeHost, ct);
        }
        catch (Exception exception) when (exception is SocketException or ArgumentException)
        {
            throw new ValidationException("Webhook target host could not be resolved.");
        }

        if (addresses.Length == 0)
            throw new ValidationException("Webhook target host could not be resolved.");
        if (isHttp && addresses.Any(address => !IsLoopback(address)))
            throw new ValidationException("Plain HTTP webhook targets are restricted to loopback addresses.");
        if (addresses.Any(address => !IsAllowed(address, allowLoopback)))
            throw new ValidationException("Webhook target resolves to a prohibited network address.");
        return new WebhookResolvedTarget(uri, addresses);
    }

    private static bool IsAllowed(IPAddress address, bool allowLoopback)
    {
        if (IsLoopback(address)) return allowLoopback;
        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
        var bytes = address.GetAddressBytes();
        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            return bytes[0] != 0
                && bytes[0] != 10
                && bytes[0] != 127
                && !(bytes[0] == 169 && bytes[1] == 254)
                && !(bytes[0] == 172 && bytes[1] is >= 16 and <= 31)
                && !(bytes[0] == 100 && bytes[1] is >= 64 and <= 127)
                && !(bytes[0] == 192 && bytes[1] == 0 && bytes[2] is 0 or 2)
                && !(bytes[0] == 192 && bytes[1] == 168)
                && !(bytes[0] == 198 && bytes[1] is 18 or 19)
                && !(bytes[0] == 198 && bytes[1] == 51 && bytes[2] == 100)
                && !(bytes[0] == 203 && bytes[1] == 0 && bytes[2] == 113)
                && !(bytes[0] >= 224);
        }

        return !address.IsIPv6LinkLocal
            && !address.IsIPv6Multicast
            && !address.IsIPv6SiteLocal
            && !(bytes[0] is 0xfc or 0xfd)
            && !(bytes[0] == 0x20 && bytes[1] == 0x01 && bytes[2] == 0x0d && bytes[3] == 0xb8)
            && !address.Equals(IPAddress.IPv6None)
            && !address.Equals(IPAddress.IPv6Any);
    }

    private static bool IsLoopback(IPAddress address) =>
        IPAddress.IsLoopback(address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address);
}
