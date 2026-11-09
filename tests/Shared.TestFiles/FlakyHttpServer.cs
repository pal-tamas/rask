using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Rask.TestFiles;

/// <summary>
///     A release host for a fetch test, on a loopback port of its own: answers the statuses it is given in turn
///     (a 504, say), then the file, and counts every request — so a test can prove a cache hit asked nothing.
/// </summary>
/// <remarks>
///     A raw <see cref="TcpListener" /> on port 0 rather than <c>HttpListener</c>, which cannot take an ephemeral
///     port and so collides between parallel test classes. One request per connection, <c>Connection: close</c>.
/// </remarks>
internal sealed class FlakyHttpServer : IAsyncDisposable
{
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly Queue<int> _failures;
    private readonly byte[] _body;
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _serving;
    private int _requests;

    public FlakyHttpServer(byte[] body, params int[] failuresFirst)
    {
        _body = body;
        _failures = new Queue<int>(failuresFirst);
        _listener.Start();
        _serving = Task.Run(ServeAsync);
    }

    /// <summary>The base URL, without a trailing slash; any path under it answers.</summary>
    public string BaseUrl => $"http://127.0.0.1:{((IPEndPoint)_listener.LocalEndpoint).Port}";

    /// <summary>How many requests have been answered.</summary>
    public int Requests => Volatile.Read(ref _requests);

    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync();
        _listener.Stop();
        try
        {
            await _serving;
        }
        catch (Exception ex) when (ex is OperationCanceledException or SocketException or ObjectDisposedException)
        {
            // Stopping the listener is how the loop ends.
        }

        _stop.Dispose();
    }

    private async Task ServeAsync()
    {
        while (!_stop.IsCancellationRequested)
        {
            using var client = await _listener.AcceptTcpClientAsync(_stop.Token);
            await using var stream = client.GetStream();
            await ReadRequestHeadAsync(stream);
            Interlocked.Increment(ref _requests);

            var status = _failures.Count > 0 ? _failures.Dequeue() : 200;
            var body = status == 200 ? _body : Encoding.ASCII.GetBytes("upstream timed out");
            var head = $"HTTP/1.1 {status} {(status == 200 ? "OK" : "Failed")}\r\nContent-Length: {body.Length}\r\n"
                       + "Content-Type: application/octet-stream\r\nConnection: close\r\n\r\n";
            await stream.WriteAsync(Encoding.ASCII.GetBytes(head), _stop.Token);
            await stream.WriteAsync(body, _stop.Token);
        }
    }

    private async Task ReadRequestHeadAsync(NetworkStream stream)
    {
        // Up to the blank line that ends a GET's head; a GET has no body.
        var seen = new List<byte>();
        var one = new byte[1];
        while (await stream.ReadAsync(one, _stop.Token) == 1)
        {
            seen.Add(one[0]);
            if (seen.Count >= 4 && seen[^1] == '\n' && seen[^2] == '\r' && seen[^3] == '\n' && seen[^4] == '\r')
            {
                return;
            }
        }
    }
}
