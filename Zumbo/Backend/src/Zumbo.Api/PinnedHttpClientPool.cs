using System.Net;
using System.Net.Sockets;

internal sealed class PinnedHttpClientPool : IDisposable
{
    private const int MaximumCachedClients = 128;
    private readonly Dictionary<string, HttpClient> clients = new(StringComparer.Ordinal);
    private readonly object gate = new();
    private bool disposed;

    internal int CachedClientCount
    {
        get
        {
            lock (gate) return clients.Count;
        }
    }

    public PinnedHttpClientLease Rent(
        Uri target,
        IReadOnlyList<IPAddress> addresses,
        TimeSpan timeout,
        Func<Exception> connectFailure)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(addresses);
        ArgumentNullException.ThrowIfNull(connectFailure);
        if (addresses.Count == 0) throw new ArgumentException("At least one pinned address is required.", nameof(addresses));

        var key = BuildKey(target, addresses, timeout);
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (clients.TryGetValue(key, out var cached))
            {
                return new PinnedHttpClientLease(cached, ownsClient: false);
            }

            var created = CreateClient(target, addresses, timeout, connectFailure);
            if (clients.Count < MaximumCachedClients)
            {
                clients.Add(key, created);
                return new PinnedHttpClientLease(created, ownsClient: false);
            }

            return new PinnedHttpClientLease(created, ownsClient: true);
        }
    }

    public void Dispose()
    {
        lock (gate)
        {
            if (disposed) return;
            disposed = true;
            foreach (var client in clients.Values) client.Dispose();
            clients.Clear();
        }
    }

    private static HttpClient CreateClient(
        Uri target,
        IReadOnlyList<IPAddress> addresses,
        TimeSpan timeout,
        Func<Exception> connectFailure)
    {
        var expectedHost = target.DnsSafeHost.TrimEnd('.');
        var expectedPort = target.Port;
        var pinnedAddresses = addresses.ToArray();
        var handler = new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            AutomaticDecompression = DecompressionMethods.None,
            UseCookies = false,
            UseProxy = false,
            ConnectTimeout = timeout,
            PooledConnectionIdleTimeout = TimeSpan.FromMinutes(1),
            PooledConnectionLifetime = TimeSpan.FromMinutes(2),
            MaxConnectionsPerServer = 32,
            ConnectCallback = async (context, cancellationToken) =>
            {
                if (context.DnsEndPoint.Port != expectedPort
                    || !string.Equals(
                        context.DnsEndPoint.Host.TrimEnd('.'),
                        expectedHost,
                        StringComparison.OrdinalIgnoreCase))
                {
                    throw connectFailure();
                }

                foreach (var address in pinnedAddresses)
                {
                    var socket = new Socket(
                        address.AddressFamily,
                        SocketType.Stream,
                        ProtocolType.Tcp)
                    {
                        NoDelay = true
                    };
                    try
                    {
                        await socket.ConnectAsync(address, expectedPort, cancellationToken);
                        return new NetworkStream(socket, ownsSocket: true);
                    }
                    catch (Exception exception) when (
                        exception is SocketException or OperationCanceledException)
                    {
                        socket.Dispose();
                        if (exception is OperationCanceledException) throw;
                    }
                }

                throw connectFailure();
            }
        };
        return new HttpClient(handler)
        {
            Timeout = timeout
        };
    }

    private static string BuildKey(
        Uri target,
        IReadOnlyList<IPAddress> addresses,
        TimeSpan timeout)
    {
        var addressKey = string.Join(
            ',',
            addresses
                .Select(address => address.ToString())
                .Order(StringComparer.Ordinal));
        return string.Join(
            '|',
            target.Scheme.ToLowerInvariant(),
            target.DnsSafeHost.TrimEnd('.').ToLowerInvariant(),
            target.Port,
            timeout.Ticks,
            addressKey);
    }
}

internal readonly struct PinnedHttpClientLease(
    HttpClient client,
    bool ownsClient) : IDisposable
{
    public HttpClient Client { get; } = client;

    public void Dispose()
    {
        if (ownsClient) Client.Dispose();
    }
}
