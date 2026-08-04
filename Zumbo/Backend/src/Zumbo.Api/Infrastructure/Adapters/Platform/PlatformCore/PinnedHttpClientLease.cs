using System.Net;
using System.Net.Sockets;

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
