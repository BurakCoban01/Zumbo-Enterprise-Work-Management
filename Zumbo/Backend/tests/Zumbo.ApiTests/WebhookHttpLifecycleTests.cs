using System.Net;
using System.Net.Sockets;
using System.Text;
using Microsoft.Extensions.Options;
using Zumbo.Modules.WorkItems;
using Zumbo.SharedKernel;

namespace Zumbo.ApiTests;

public sealed class WebhookHttpLifecycleTests
{
    [Fact]
    public async Task SequentialWebhookDeliveries_ReusePinnedKeepAliveConnection()
    {
        await using var receiver = new KeepAliveLoopbackReceiver(HttpStatusCode.NoContent, string.Empty);
        var options = Options.Create(new WebhookOptions
        {
            AllowHttpLoopback = true,
            RequestTimeoutSeconds = 3
        });
        var policy = new WebhookTargetPolicy(options);
        using var sender = new PinnedWebhookSender(policy, options);

        await sender.SendAsync(Request(receiver.Url, "delivery-1"), CancellationToken.None);
        await sender.SendAsync(Request(receiver.Url, "delivery-2"), CancellationToken.None);
        await receiver.WaitForRequestsAsync(2, TimeSpan.FromSeconds(5));

        Assert.Equal(1, receiver.AcceptedConnections);
    }

    [Fact]
    public async Task SequentialDevelopmentProviderProbes_ReusePinnedKeepAliveConnection()
    {
        await using var receiver = new KeepAliveLoopbackReceiver(HttpStatusCode.OK, "{}");
        var options = Options.Create(new DevelopmentProviderOptions
        {
            AllowHttpLoopback = true,
            AllowedHosts = ["127.0.0.1"],
            RequestTimeoutSeconds = 3
        });
        using var gateway = new DevelopmentProviderGateway(
            new DevelopmentProviderTargetPolicy(options),
            options);

        var first = await gateway.ProbeAsync(
            DevelopmentProviders.GitHub,
            receiver.BaseUrl,
            "synthetic-token",
            CancellationToken.None);
        var second = await gateway.ProbeAsync(
            DevelopmentProviders.GitHub,
            receiver.BaseUrl,
            "synthetic-token",
            CancellationToken.None);
        await receiver.WaitForRequestsAsync(2, TimeSpan.FromSeconds(5));

        Assert.True(first.Healthy);
        Assert.True(second.Healthy);
        Assert.Equal(1, receiver.AcceptedConnections);
    }

    [Fact]
    public async Task RedirectsRemainDisabledAndCannotReachAnotherTarget()
    {
        await using var destination = new KeepAliveLoopbackReceiver(
            HttpStatusCode.NoContent,
            string.Empty);
        await using var redirect = new KeepAliveLoopbackReceiver(
            HttpStatusCode.Found,
            string.Empty,
            responseHeaders: new Dictionary<string, string>
            {
                ["Location"] = destination.Url
            });
        var options = Options.Create(new WebhookOptions
        {
            AllowHttpLoopback = true,
            RequestTimeoutSeconds = 3
        });
        using var sender = new PinnedWebhookSender(
            new WebhookTargetPolicy(options),
            options);

        var exception = await Assert.ThrowsAsync<WebhookDeliveryException>(() =>
            sender.SendAsync(Request(redirect.Url, "redirect"), CancellationToken.None));

        Assert.Equal("HTTP_302", exception.SafeCode);
        Assert.Equal(1, redirect.AcceptedConnections);
        Assert.Equal(0, destination.AcceptedConnections);
    }

    [Fact]
    public async Task RequestTimeoutRemainsBoundedAndUsesSafeCode()
    {
        await using var receiver = new KeepAliveLoopbackReceiver(
            HttpStatusCode.NoContent,
            string.Empty,
            responseDelay: TimeSpan.FromSeconds(2));
        var options = Options.Create(new WebhookOptions
        {
            AllowHttpLoopback = true,
            RequestTimeoutSeconds = 1
        });
        using var sender = new PinnedWebhookSender(
            new WebhookTargetPolicy(options),
            options);

        var exception = await Assert.ThrowsAsync<WebhookDeliveryException>(() =>
            sender.SendAsync(Request(receiver.Url, "timeout"), CancellationToken.None));

        Assert.Equal("REQUEST_TIMEOUT", exception.SafeCode);
    }

    [Fact]
    public async Task DevelopmentProviderResponseBodyRemainsBounded()
    {
        await using var receiver = new KeepAliveLoopbackReceiver(
            HttpStatusCode.OK,
            new string('x', 2_048));
        var options = Options.Create(new DevelopmentProviderOptions
        {
            AllowHttpLoopback = true,
            AllowedHosts = ["127.0.0.1"],
            RequestTimeoutSeconds = 3,
            MaximumResponseBytes = 1_024
        });
        using var gateway = new DevelopmentProviderGateway(
            new DevelopmentProviderTargetPolicy(options),
            options);

        var exception = await Assert.ThrowsAsync<ConflictException>(() =>
            gateway.ListRepositoriesAsync(
                DevelopmentProviders.GitHub,
                receiver.BaseUrl,
                "synthetic-token",
                10,
                CancellationToken.None));

        Assert.Equal("DEVELOPMENT_PROVIDER_UNAVAILABLE", exception.Code);
        Assert.DoesNotContain("synthetic-token", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AddressFingerprintChangesUseSeparateClientsAndCacheIsBounded()
    {
        using var pool = new PinnedHttpClientPool();
        var target = new Uri("http://127.0.0.1:50000");
        using var first = pool.Rent(
            target,
            [IPAddress.Loopback],
            TimeSpan.FromSeconds(3),
            () => new InvalidOperationException());
        using var same = pool.Rent(
            target,
            [IPAddress.Loopback],
            TimeSpan.FromSeconds(3),
            () => new InvalidOperationException());
        using var changed = pool.Rent(
            target,
            [IPAddress.IPv6Loopback],
            TimeSpan.FromSeconds(3),
            () => new InvalidOperationException());

        Assert.Same(first.Client, same.Client);
        Assert.NotSame(first.Client, changed.Client);

        for (var index = 0; index < 140; index++)
        {
            using var lease = pool.Rent(
                new Uri($"http://127.0.0.1:{51000 + index}"),
                [IPAddress.Loopback],
                TimeSpan.FromSeconds(3),
                () => new InvalidOperationException());
        }

        Assert.Equal(128, pool.CachedClientCount);
    }

    private static WebhookSendRequest Request(string targetUrl, string deliveryId) =>
        new(
            targetUrl,
            "{\"event\":\"lifecycle\"}",
            deliveryId,
            1,
            1,
            "synthetic-signature",
            null,
            null);

    private sealed class KeepAliveLoopbackReceiver : IAsyncDisposable
    {
        private readonly TcpListener listener = new(IPAddress.Loopback, 0);
        private readonly CancellationTokenSource stopping = new();
        private readonly HttpStatusCode statusCode;
        private readonly byte[] body;
        private readonly IReadOnlyDictionary<string, string> responseHeaders;
        private readonly TimeSpan responseDelay;
        private readonly Task serverTask;
        private int acceptedConnections;
        private int requests;

        public KeepAliveLoopbackReceiver(
            HttpStatusCode statusCode,
            string body,
            IReadOnlyDictionary<string, string>? responseHeaders = null,
            TimeSpan? responseDelay = null)
        {
            this.statusCode = statusCode;
            this.body = Encoding.UTF8.GetBytes(body);
            this.responseHeaders = responseHeaders
                ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            this.responseDelay = responseDelay ?? TimeSpan.Zero;
            listener.Start();
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            BaseUrl = $"http://127.0.0.1:{port}";
            Url = BaseUrl + "/webhooks";
            serverTask = RunAsync(stopping.Token);
        }

        public string BaseUrl { get; }
        public string Url { get; }
        public int AcceptedConnections => Volatile.Read(ref acceptedConnections);

        public async Task WaitForRequestsAsync(int expected, TimeSpan timeout)
        {
            using var cancellation = new CancellationTokenSource(timeout);
            while (Volatile.Read(ref requests) < expected)
            {
                await Task.Delay(10, cancellation.Token);
            }
        }

        public async ValueTask DisposeAsync()
        {
            stopping.Cancel();
            listener.Stop();
            try
            {
                await serverTask;
            }
            catch (OperationCanceledException)
            {
            }
            stopping.Dispose();
        }

        private async Task RunAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                TcpClient client;
                try
                {
                    client = await listener.AcceptTcpClientAsync(ct);
                }
                catch (Exception exception) when (
                    exception is OperationCanceledException or SocketException)
                {
                    return;
                }

                Interlocked.Increment(ref acceptedConnections);
                _ = HandleAsync(client, ct);
            }
        }

        private async Task HandleAsync(TcpClient client, CancellationToken ct)
        {
            using (client)
            await using (var stream = client.GetStream())
            using (var reader = new StreamReader(
                stream,
                Encoding.ASCII,
                detectEncodingFromByteOrderMarks: false,
                bufferSize: 4_096,
                leaveOpen: true))
            {
                while (!ct.IsCancellationRequested)
                {
                    string? requestLine;
                    try
                    {
                        requestLine = await reader.ReadLineAsync(ct);
                    }
                    catch (Exception exception) when (
                        exception is OperationCanceledException or IOException)
                    {
                        return;
                    }
                    if (string.IsNullOrEmpty(requestLine)) return;

                    var contentLength = 0;
                    while (await reader.ReadLineAsync(ct) is { Length: > 0 } line)
                    {
                        if (line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
                        {
                            contentLength = int.Parse(line["Content-Length:".Length..].Trim());
                        }
                    }

                    var remaining = contentLength;
                    var buffer = new char[Math.Min(Math.Max(contentLength, 1), 4_096)];
                    while (remaining > 0)
                    {
                        var read = await reader.ReadAsync(
                            buffer.AsMemory(0, Math.Min(buffer.Length, remaining)),
                            ct);
                        if (read == 0) return;
                        remaining -= read;
                    }

                    var reason = statusCode switch
                    {
                        HttpStatusCode.OK => "OK",
                        HttpStatusCode.NoContent => "No Content",
                        HttpStatusCode.Found => "Found",
                        _ => "Response"
                    };
                    if (responseDelay > TimeSpan.Zero)
                    {
                        await Task.Delay(responseDelay, ct);
                    }
                    var additionalHeaders = string.Concat(
                        responseHeaders.Select(header =>
                            $"{header.Key}: {header.Value}\r\n"));
                    var responseBytes = Encoding.ASCII.GetBytes(
                        $"HTTP/1.1 {(int)statusCode} {reason}\r\n" +
                        $"Content-Length: {body.Length}\r\n" +
                        "Content-Type: application/json\r\n" +
                        additionalHeaders +
                        "Connection: keep-alive\r\n\r\n");
                    await stream.WriteAsync(responseBytes, ct);
                    if (body.Length > 0) await stream.WriteAsync(body, ct);
                    await stream.FlushAsync(ct);
                    Interlocked.Increment(ref requests);
                }
            }
        }
    }
}
