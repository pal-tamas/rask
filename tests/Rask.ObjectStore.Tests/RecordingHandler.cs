using System.Net;

namespace Rask.ObjectStore.Tests;

/// <summary>What one request looked like by the time it reached the wire.</summary>
internal sealed record Recorded(
    HttpMethod Method,
    Uri Uri,
    IReadOnlyDictionary<string, string> Headers,
    byte[] Body)
{
    public string? Header(string name) => Headers.TryGetValue(name, out var value) ? value : null;
}

/// <summary>
///     Replays queued responses and records what was sent. Everything the stores are asserted on — the URL
///     they built, the headers they signed, the bytes they wrote — is captured here rather than inferred,
///     because these are exactly the details a real service would reject silently.
/// </summary>
internal sealed class RecordingHandler : HttpMessageHandler
{
    private readonly Queue<HttpResponseMessage> _responses = new();

    public List<Recorded> Requests { get; } = [];

    public Recorded Last => Requests[^1];

    public RecordingHandler Respond(HttpStatusCode status, string? body = null, DateTimeOffset? date = null)
    {
        var response = new HttpResponseMessage(status);
        if (body is not null)
        {
            response.Content = new StringContent(body);
        }

        if (date is not null)
        {
            response.Headers.Date = date;
        }

        _responses.Enqueue(response);
        return this;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        // Captured now rather than kept as a live message: HttpClient disposes the request once it returns,
        // taking the content and headers with it.
        var headers = request.Headers.ToDictionary(
            static h => h.Key.ToLowerInvariant(),
            static h => string.Join(",", h.Value),
            StringComparer.OrdinalIgnoreCase);

        var body = Array.Empty<byte>();
        if (request.Content is not null)
        {
            body = await request.Content.ReadAsByteArrayAsync(cancellationToken);
            foreach (var header in request.Content.Headers)
            {
                headers[header.Key.ToLowerInvariant()] = string.Join(",", header.Value);
            }
        }

        Requests.Add(new Recorded(request.Method, request.RequestUri!, headers, body));

        return _responses.Count > 0
            ? _responses.Dequeue()
            : throw new InvalidOperationException(
                $"No response queued for {request.Method} {request.RequestUri}. " +
                $"{Requests.Count} request(s) have been made.");
    }
}
