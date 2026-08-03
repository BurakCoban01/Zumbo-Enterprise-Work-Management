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

public sealed class DevelopmentProviderTargetPolicy(
    IOptions<DevelopmentProviderOptions> options)
{
    public async Task<DevelopmentProviderResolvedTarget> ResolveAsync(
        string provider,
        string baseUrl,
        CancellationToken ct)
    {
        if (!DevelopmentProviders.All.Contains(provider)
            || !Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri)
            || !string.IsNullOrEmpty(uri.UserInfo)
            || !string.IsNullOrEmpty(uri.Query)
            || !string.IsNullOrEmpty(uri.Fragment)
            || string.IsNullOrWhiteSpace(uri.Host))
        {
            throw new ValidationException(
                "Development provider base URL is not valid.");
        }

        var settings = options.Value;
        var allowedHosts = settings.AllowedHosts
            .Where(host => !string.IsNullOrWhiteSpace(host))
            .Select(host => host.Trim().TrimEnd('.'))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (allowedHosts.Count == 0
            || allowedHosts.Contains("*")
            || !allowedHosts.Contains(uri.DnsSafeHost.TrimEnd('.')))
        {
            throw new ValidationException(
                "Development provider host is not allowlisted.");
        }

        var isHttps = uri.Scheme.Equals(
            Uri.UriSchemeHttps,
            StringComparison.OrdinalIgnoreCase);
        var isHttp = uri.Scheme.Equals(
            Uri.UriSchemeHttp,
            StringComparison.OrdinalIgnoreCase);
        if (!isHttps && !(isHttp && settings.AllowHttpLoopback))
        {
            throw new ValidationException(
                "Development provider base URL must use HTTPS.");
        }

        IPAddress[] addresses;
        try
        {
            addresses = await Dns.GetHostAddressesAsync(uri.DnsSafeHost, ct);
        }
        catch (Exception exception) when (
            exception is SocketException or ArgumentException)
        {
            throw new ValidationException(
                "Development provider host could not be resolved.");
        }

        if (addresses.Length == 0)
        {
            throw new ValidationException(
                "Development provider host could not be resolved.");
        }
        if (isHttp && addresses.Any(address => !IsLoopback(address)))
        {
            throw new ValidationException(
                "Plain HTTP development providers are restricted to loopback.");
        }
        if (addresses.Any(address =>
                !IsAllowedAddress(address, settings)))
        {
            throw new ValidationException(
                "Development provider resolves to a prohibited network address.");
        }

        return new DevelopmentProviderResolvedTarget(uri, addresses);
    }

    private static bool IsAllowedAddress(
        IPAddress address,
        DevelopmentProviderOptions options)
    {
        if (IsLoopback(address)) return options.AllowHttpLoopback;
        if (options.AllowPrivateNetworkHosts) return true;
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
            && !(bytes[0] == 0x20
                && bytes[1] == 0x01
                && bytes[2] == 0x0d
                && bytes[3] == 0xb8)
            && !address.Equals(IPAddress.IPv6None)
            && !address.Equals(IPAddress.IPv6Any);
    }

    private static bool IsLoopback(IPAddress address) =>
        IPAddress.IsLoopback(
            address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address);
}
