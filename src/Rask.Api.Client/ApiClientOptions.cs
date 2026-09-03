namespace Rask.Api.Client;

/// <summary>
///     Configures how generated API clients send their requests.
/// </summary>
public sealed class ApiClientOptions
{
    private TimeSpan _timeout = TimeSpan.FromSeconds(100);

    /// <summary>
    ///     Where the API lives. Leave null in a browser app, where the page origin is already the right
    ///     answer and the container's <see cref="HttpClient" /> carries it.
    /// </summary>
    public Uri? BaseAddress { get; set; }

    /// <summary>
    ///     Runs on every outgoing request — the hook for a bearer token, a tenant header, a trace id.
    /// </summary>
    /// <remarks>
    ///     It receives the <em>request</em> rather than an <see cref="HttpClient" />, deliberately.
    ///     Attaching a token to the client makes it ambient state shared by everything that resolves the
    ///     same client; attaching it here scopes it to the call being made.
    /// </remarks>
    public Func<HttpRequestMessage, CancellationToken, Task>? ConfigureRequestAsync { get; set; }

    /// <summary>
    ///     How long one call may take. Applied per attempt.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not positive.</exception>
    public TimeSpan Timeout
    {
        get => _timeout;
        set
        {
            if (value <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(value), value, "The API client timeout must be greater than zero.");
            }

            _timeout = value;
        }
    }
}
