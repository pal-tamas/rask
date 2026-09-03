using System.Net.Http.Headers;
using System.Text.Json;

namespace Rask.Api.Client;

/// <summary>
///     Sends one API request and hands back its body. Called by generated client code; you do not use it
///     directly.
/// </summary>
/// <remarks>
///     Everything a call does that is not shape-specific lives here rather than in generated text: the
///     auth hook, the timeout, and the mapping from a failure response to <see cref="ApiException" />.
///     The generated method keeps only what it alone knows — the route, the verb, and the codec pair —
///     so a fix to any of this reaches every client without regenerating anything.
/// </remarks>
public static class ApiCall
{
    private static readonly MediaTypeHeaderValue Json = new("application/json");

    /// <summary>
    ///     Sends the request and returns its body, or null when the answer had none.
    /// </summary>
    /// <param name="http">The client to send on.</param>
    /// <param name="options">The client options.</param>
    /// <param name="method">The HTTP method.</param>
    /// <param name="path">The path and query, relative to the client's base address.</param>
    /// <param name="body">A JSON request body, or null for a request that carries none.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The response body, or null for a 204 or an empty answer.</returns>
    /// <exception cref="ApiException">The call failed, or the answer was not a success status.</exception>
    public static async Task<byte[]?> SendAsync(
        HttpClient http,
        ApiClientOptions options,
        HttpMethod method,
        string path,
        byte[]? body,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(method);
        ArgumentNullException.ThrowIfNull(path);

        // Resolved once, and reported. An exception naming the RELATIVE path would make a caller
        // guess which host and prefix it actually reached — the very thing a base address decides.
        var uri = Resolve(http.BaseAddress, path);
        var reported = uri.IsAbsoluteUri ? uri.ToString() : path;

        using var request = new HttpRequestMessage(method, uri);

        // Say JSON explicitly. Without it, content negotiation hands a `string`-returning action to
        // ASP.NET's StringOutputFormatter, which answers text/plain — so `return "ok"` arrives as the
        // five bytes `ok` rather than the seven of `"ok"`, and the decoder fails on a valid response.
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        if (body is not null)
        {
            request.Content = new ByteArrayContent(body) { Headers = { ContentType = Json } };
        }

        if (options.ConfigureRequestAsync is not null)
        {
            await options.ConfigureRequestAsync(request, cancellationToken).ConfigureAwait(false);
        }

        // The timeout is applied per attempt here rather than on the HttpClient, because on the path
        // most apps take the client is not ours to configure: a browser app reuses the container's
        // HttpClient, whose BaseAddress is the page origin.
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(options.Timeout);

        HttpResponseMessage response;

        try
        {
            response = await http.SendAsync(request, timeout.Token).ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            throw Unreachable(method, reported, ex);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            // The caller did not cancel, so this is our own timeout rather than an abandoned call.
            throw Unreachable(method, reported, ex);
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                throw await FailureAsync(response, method, reported, cancellationToken).ConfigureAwait(false);
            }

            if (response.StatusCode == System.Net.HttpStatusCode.NoContent)
            {
                return null;
            }

            var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
            return bytes.Length == 0 ? null : bytes;
        }
    }

    /// <summary>
    ///     Combines the relative path a generated client built with the client's base address.
    /// </summary>
    /// <remarks>
    ///     The trailing slash is the point. RFC 3986 resolution treats the last segment of a base
    ///     address as a <em>file</em> unless it ends in <c>/</c>, so a base of
    ///     <c>https://host/myapp</c> combined with <c>api/posts</c> gives <c>https://host/api/posts</c> —
    ///     the sub-path silently dropped, which is the failure this whole relative-path arrangement
    ///     exists to prevent. Adding it back makes the combination mean what an app deployed under a
    ///     prefix expects.
    /// </remarks>
    private static Uri Resolve(Uri? baseAddress, string path)
    {
        if (baseAddress is null)
        {
            return new Uri(path, UriKind.Relative);
        }

        var root = baseAddress.AbsolutePath.EndsWith('/')
            ? baseAddress
            : new UriBuilder(baseAddress) { Path = baseAddress.AbsolutePath + "/" }.Uri;

        return new Uri(root, path);
    }

    private static ApiException Unreachable(HttpMethod method, string path, Exception cause) =>
        new(
            $"{method} {path} could not be sent: {cause.Message}",
            method.Method,
            path,
            statusCode: null,
            innerException: cause);

    private static async Task<ApiException> FailureAsync(
        HttpResponseMessage response,
        HttpMethod method,
        string path,
        CancellationToken cancellationToken)
    {
        string? problemType = null;
        string? detail = null;

        var mediaType = response.Content.Headers.ContentType?.MediaType;

        if (mediaType is "application/problem+json" or "application/json")
        {
            try
            {
                var bytes = await response.Content
                    .ReadAsByteArrayAsync(cancellationToken)
                    .ConfigureAwait(false);

                ReadProblem(bytes, ref problemType, ref detail);
            }
            catch (JsonException)
            {
                // A body that claimed to be a problem document and was not tells us nothing useful.
                // The status still does, so report that rather than replacing it with a parse error.
            }
        }

        var summary = detail is null
            ? $"{method} {path} answered {(int)response.StatusCode}."
            : $"{method} {path} answered {(int)response.StatusCode}: {detail}";

        return new ApiException(summary, method.Method, path, (int)response.StatusCode, problemType, detail);
    }

    // Hand-read rather than deserialized: two known fields, and it keeps the package free of a
    // reflection-based path the trimmer would have to be told about.
    private static void ReadProblem(ReadOnlySpan<byte> json, ref string? type, ref string? detail)
    {
        var reader = new Utf8JsonReader(json);

        if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject)
        {
            return;
        }

        // Skip() rather than hand-tracked depth. The previous version consumed a nested object or
        // array as a property VALUE without counting it, so its closing token read as top-level and
        // parsing stopped there: for an ASP.NET validation problem — {"errors":{...},"detail":"..."} —
        // detail was never reached, and {"errors":{"detail":"inner"},"detail":"real"} reported the
        // inner one. The BCL already knows how to step over a value; it does not need help.
        while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
        {
            var isType = reader.ValueTextEquals("type");
            var isDetail = reader.ValueTextEquals("detail");

            if (!reader.Read())
            {
                return;
            }

            if (reader.TokenType is JsonTokenType.StartObject or JsonTokenType.StartArray)
            {
                reader.Skip();
                continue;
            }

            if (reader.TokenType != JsonTokenType.String)
            {
                continue;
            }

            if (isType)
            {
                type = reader.GetString();
            }
            else if (isDetail)
            {
                detail = reader.GetString();
            }
        }
    }
}
