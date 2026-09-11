using System.Net;
using System.Text;

namespace Rask.Storage.Tests;

/// <summary>What one request looked like by the time it reached the wire.</summary>
internal sealed record Recorded(HttpMethod Method, Uri Uri, IReadOnlyDictionary<string, string> Headers, byte[] Body)
{
    public string? Header(string name) => Headers.TryGetValue(name, out var value) ? value : null;
}

/// <summary>
///     Replays queued responses and records what was sent — the URL built, the headers signed, the bytes written —
///     because these are exactly the details a real service rejects silently. Salvaged from the withdrawn
///     Rask.ObjectStore tests.
/// </summary>
internal sealed class RecordingHandler : HttpMessageHandler
{
    private readonly Queue<HttpResponseMessage> _responses = new();

    public List<Recorded> Requests { get; } = [];

    public Recorded Last => Requests[^1];

    public RecordingHandler Respond(HttpStatusCode status, string? body = null, params (string Name, string Value)[] headers)
    {
        var response = new HttpResponseMessage(status);
        if (body is not null)
        {
            response.Content = new ByteArrayContent(Encoding.UTF8.GetBytes(body));
        }

        foreach (var (name, value) in headers)
        {
            response.Headers.TryAddWithoutValidation(name, value);
        }

        _responses.Enqueue(response);
        return this;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        // Captured now: HttpClient disposes the request once it returns, taking the content and headers with it.
        var headers = request.Headers.NonValidated.ToDictionary(
            static h => h.Key.ToLowerInvariant(),
            static h => string.Join(",", h.Value),
            StringComparer.OrdinalIgnoreCase);

        var body = Array.Empty<byte>();
        if (request.Content is not null)
        {
            body = await request.Content.ReadAsByteArrayAsync(cancellationToken);
            foreach (var header in request.Content.Headers.NonValidated)
            {
                headers[header.Key.ToLowerInvariant()] = string.Join(",", header.Value);
            }
        }

        Requests.Add(new Recorded(request.Method, request.RequestUri!, headers, body));

        return _responses.Count > 0
            ? _responses.Dequeue()
            : throw new InvalidOperationException(
                $"No response queued for {request.Method} {request.RequestUri}. {Requests.Count} request(s) have been made.");
    }
}
