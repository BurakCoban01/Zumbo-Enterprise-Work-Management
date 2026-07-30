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

public sealed class DevelopmentCredentialProtectorAdapter(
    IDataProtectionProvider provider) : IDevelopmentCredentialProtector
{
    private readonly IDataProtector protector =
        provider.CreateProtector("Zumbo.WorkItems.DevelopmentCredential.v1");

    public string Protect(string value) => protector.Protect(value);
    public string Unprotect(string value) => protector.Unprotect(value);
}

public sealed class DevelopmentIntegrationAuthorizationAdapter(
    IWebhookAuthorization authorization) : IDevelopmentIntegrationAuthorization
{
    public Task EnsureCanManageAsync(string organizationId, CancellationToken ct) =>
        authorization.EnsureCanManageAsync(organizationId, ct);
}

public sealed class DevelopmentProjectDirectoryAdapter(
    IDocumentRepository<ProjectDocument> projects,
    IProjectResourcePolicy resourcePolicy) : IDevelopmentProjectDirectory
{
    public async Task<DevelopmentProjectResource> GetAsync(
        string organizationId,
        string projectId,
        CancellationToken ct)
    {
        var access = await resourcePolicy.AuthorizeAsync(
            projectId,
            PermissionCatalog.WorkItemLink,
            ct);
        if (!string.Equals(access.OrganizationId, organizationId, StringComparison.Ordinal))
        {
            throw new NotFoundException(
                "PROJECT_NOT_FOUND",
                "Project was not found.");
        }

        var project = await projects.SelectAsync(
            item => item.Id == projectId
                && item.OrganizationId == organizationId
                && !item.Archived,
            ct) ?? throw new NotFoundException(
                "PROJECT_NOT_FOUND",
                "Project was not found.");
        return new DevelopmentProjectResource(
            project.OrganizationId,
            project.Id,
            project.Key,
            project.Name);
    }
}

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

public sealed record DevelopmentProviderResolvedTarget(
    Uri BaseUri,
    IReadOnlyList<IPAddress> Addresses);

public sealed class DevelopmentProviderGateway(
    DevelopmentProviderTargetPolicy targetPolicy,
    IOptions<DevelopmentProviderOptions> options) : IDevelopmentProviderGateway, IDisposable
{
    private readonly PinnedHttpClientPool clientPool = new();

    public async Task ValidateBaseUrlAsync(
        string provider,
        string baseUrl,
        CancellationToken ct)
    {
        _ = await targetPolicy.ResolveAsync(provider, baseUrl, ct);
    }

    public async Task<DevelopmentProviderProbeResult> ProbeAsync(
        string provider,
        string baseUrl,
        string accessToken,
        CancellationToken ct)
    {
        try
        {
            var response = await GetAsync(
                provider,
                baseUrl,
                accessToken,
                "/user",
                null,
                ct);
            return response.StatusCode is >= HttpStatusCode.OK
                and < HttpStatusCode.MultipleChoices
                ? new DevelopmentProviderProbeResult(true, null)
                : new DevelopmentProviderProbeResult(
                    false,
                    SafeErrorCode(response.StatusCode));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is HttpRequestException
                or TaskCanceledException
                or DevelopmentProviderTransportException)
        {
            return new DevelopmentProviderProbeResult(
                false,
                exception is TaskCanceledException
                    ? "REQUEST_TIMEOUT"
                    : "REQUEST_FAILED");
        }
    }

    public async Task<DevelopmentProviderRepositoryResult> ListRepositoriesAsync(
        string provider,
        string baseUrl,
        string accessToken,
        int maximumItems,
        CancellationToken ct)
    {
        var path = provider == DevelopmentProviders.GitHub
            ? "/user/repos"
            : "/projects";
        var query = provider == DevelopmentProviders.GitHub
            ? $"per_page={maximumItems}&sort=full_name&type=all"
            : $"membership=true&simple=true&per_page={maximumItems}&order_by=path_with_namespace&sort=asc";
        DevelopmentProviderHttpResponse response;
        try
        {
            response = await GetAsync(
                provider,
                baseUrl,
                accessToken,
                path,
                query,
                ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is HttpRequestException
                or TaskCanceledException
                or DevelopmentProviderTransportException)
        {
            throw new ConflictException(
                "DEVELOPMENT_PROVIDER_UNAVAILABLE",
                "Development provider repositories could not be read.");
        }

        if (response.StatusCode is < HttpStatusCode.OK
            or >= HttpStatusCode.MultipleChoices)
        {
            throw new ConflictException(
                SafeErrorCode(response.StatusCode),
                "Development provider repositories could not be read.");
        }

        try
        {
            using var document = JsonDocument.Parse(
                response.Body,
                new JsonDocumentOptions
                {
                    AllowTrailingCommas = false,
                    CommentHandling = JsonCommentHandling.Disallow,
                    MaxDepth = 32
                });
            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                throw new JsonException();
            }

            var repositories = document.RootElement
                .EnumerateArray()
                .Select(item => ParseRepository(provider, item))
                .Where(item => item is not null)
                .Select(item => item!)
                .Take(maximumItems)
                .ToList();
            return new DevelopmentProviderRepositoryResult(
                repositories,
                response.HasMore);
        }
        catch (JsonException)
        {
            throw new ConflictException(
                "DEVELOPMENT_PROVIDER_RESPONSE_INVALID",
                "Development provider returned an invalid repository response.");
        }
    }

    private async Task<DevelopmentProviderHttpResponse> GetAsync(
        string provider,
        string baseUrl,
        string accessToken,
        string path,
        string? query,
        CancellationToken ct)
    {
        var target = await targetPolicy.ResolveAsync(provider, baseUrl, ct);
        var requestUri = BuildRequestUri(target.BaseUri, path, query);
        var timeout = TimeSpan.FromSeconds(
            Math.Clamp(options.Value.RequestTimeoutSeconds, 1, 30));
        using var clientLease = clientPool.Rent(
            target.BaseUri,
            target.Addresses,
            timeout,
            () => new DevelopmentProviderTransportException());
        var client = clientLease.Client;
        using var request = new HttpRequestMessage(HttpMethod.Get, requestUri);
        request.Headers.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.UserAgent.ParseAdd("Zumbo-DevelopmentIntegration/1.0");
        request.Headers.Authorization =
            new AuthenticationHeaderValue("Bearer", accessToken);
        if (provider == DevelopmentProviders.GitHub)
        {
            request.Headers.TryAddWithoutValidation(
                "X-GitHub-Api-Version",
                "2022-11-28");
        }

        using var response = await client.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            ct);
        var maximumBytes = Math.Clamp(
            options.Value.MaximumResponseBytes,
            1_024,
            8 * 1_024 * 1_024);
        if (response.Content.Headers.ContentLength > maximumBytes)
        {
            throw new DevelopmentProviderTransportException();
        }

        var body = await ReadBoundedAsync(
            response.Content,
            maximumBytes,
            ct);
        var hasMore = response.Headers.TryGetValues("Link", out var links)
            && links.Any(value => value.Contains("rel=\"next\"", StringComparison.OrdinalIgnoreCase))
            || response.Headers.TryGetValues("X-Next-Page", out var nextPages)
            && nextPages.Any(value => !string.IsNullOrWhiteSpace(value));
        return new DevelopmentProviderHttpResponse(
            response.StatusCode,
            body,
            hasMore);
    }

    private static Uri BuildRequestUri(
        Uri baseUri,
        string path,
        string? query)
    {
        var builder = new UriBuilder(baseUri)
        {
            Path = baseUri.AbsolutePath.TrimEnd('/') + "/" + path.TrimStart('/'),
            Query = query ?? string.Empty
        };
        return builder.Uri;
    }

    private static async Task<byte[]> ReadBoundedAsync(
        HttpContent content,
        int maximumBytes,
        CancellationToken ct)
    {
        await using var source = await content.ReadAsStreamAsync(ct);
        using var destination = new MemoryStream();
        var buffer = new byte[16_384];
        while (true)
        {
            var read = await source.ReadAsync(buffer.AsMemory(), ct);
            if (read == 0) break;
            if (destination.Length + read > maximumBytes)
            {
                throw new DevelopmentProviderTransportException();
            }
            await destination.WriteAsync(buffer.AsMemory(0, read), ct);
        }
        return destination.ToArray();
    }

    private static DevelopmentProviderRepository? ParseRepository(
        string provider,
        JsonElement item)
    {
        if (item.ValueKind != JsonValueKind.Object) return null;
        var externalId = Text(item, "id", 200);
        var name = Text(item, "name", 200);
        var fullName = Text(
            item,
            provider == DevelopmentProviders.GitHub
                ? "full_name"
                : "path_with_namespace",
            300);
        var url = Text(
            item,
            provider == DevelopmentProviders.GitHub
                ? "html_url"
                : "web_url",
            2_048);
        var defaultBranch = Text(item, "default_branch", 255);
        if (externalId is null
            || name is null
            || fullName is null
            || defaultBranch is null
            || url is null
            || !Uri.TryCreate(url, UriKind.Absolute, out var repositoryUri)
            || repositoryUri.Scheme != Uri.UriSchemeHttps
            || !string.IsNullOrWhiteSpace(repositoryUri.UserInfo)
            || !string.IsNullOrWhiteSpace(repositoryUri.Fragment))
        {
            return null;
        }

        return new DevelopmentProviderRepository(
            externalId,
            name,
            fullName,
            repositoryUri.AbsoluteUri,
            defaultBranch);
    }

    private static string? Text(
        JsonElement item,
        string property,
        int maximum)
    {
        if (!item.TryGetProperty(property, out var value)) return null;
        var text = value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.GetRawText(),
            _ => null
        };
        text = text?.Trim();
        return text is { Length: > 0 } && text.Length <= maximum
            ? text
            : null;
    }

    private static string SafeErrorCode(HttpStatusCode statusCode) =>
        statusCode switch
        {
            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden =>
                "DEVELOPMENT_PROVIDER_CREDENTIAL_REJECTED",
            HttpStatusCode.NotFound => "DEVELOPMENT_PROVIDER_API_NOT_FOUND",
            HttpStatusCode.TooManyRequests => "DEVELOPMENT_PROVIDER_RATE_LIMITED",
            >= HttpStatusCode.InternalServerError =>
                "DEVELOPMENT_PROVIDER_UNAVAILABLE",
            _ => $"DEVELOPMENT_PROVIDER_HTTP_{(int)statusCode}"
        };

    public void Dispose() => clientPool.Dispose();
}

internal sealed record DevelopmentProviderHttpResponse(
    HttpStatusCode StatusCode,
    byte[] Body,
    bool HasMore);

internal sealed class DevelopmentProviderTransportException : Exception;
