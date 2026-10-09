using System.ComponentModel;

namespace Rask;

/// <summary>
///     The work in progress — its services and its cancellation — for the calls that take neither:
///     <c>Cache.Remember(…)</c>, <c>Jobs.Enqueue(…)</c>, <c>await Product.Create(model)</c>.
/// </summary>
/// <remarks>
///     <para>
///         The hosts open it around each piece of work: an event handler, a live session's render, an HTTP
///         request, a background job. Code inside one reaches that work's services and is cancelled with
///         it, with nothing injected and no token passed. Outside every one of them there is nothing to
///         reach, and a static call says so and names the constructor to inject instead.
///     </para>
///     <para>
///         Machinery: the hosts and the batteries use it; an app does not.
///     </para>
/// </remarks>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class Ambient
{
    private static readonly AsyncLocal<IServiceProvider?> PushedServices = new();
    private static readonly AsyncLocal<CancellationToken> PushedToken = new();

    /// <summary>
    ///     Where to look when nothing was pushed in this flow — the renderer registers the render in
    ///     progress, which it tracks itself.
    /// </summary>
    public static Func<IServiceProvider?>? FallbackServices { get; set; }

    /// <summary>
    ///     Where to look for a cancellation when nothing was pushed in this flow — the renderer registers the
    ///     component whose lifecycle hook is running, which it already tracks without a write per mount.
    /// </summary>
    public static Func<CancellationToken>? FallbackToken { get; set; }

    /// <summary>The services of the work in progress, or null outside any.</summary>
    public static IServiceProvider? Services => PushedServices.Value ?? FallbackServices?.Invoke();

    /// <summary>The cancellation of the work in progress, or <see cref="CancellationToken.None" /> outside any.</summary>
    public static CancellationToken CancellationToken =>
        PushedToken.Value is { CanBeCanceled: true } pushed ? pushed : FallbackToken?.Invoke() ?? default;

    /// <summary><paramref name="token" /> when the caller passed one, else the work in progress's.</summary>
    public static CancellationToken Or(CancellationToken token) =>
        token.CanBeCanceled ? token : CancellationToken;

    /// <summary>
    ///     The <typeparamref name="T" /> of the work in progress, for a static facade — or the one message every
    ///     facade gives when there is no work, or when its battery is not running.
    /// </summary>
    /// <param name="facade">The static the app called: <c>Jobs</c>.</param>
    /// <param name="unless">What switches the battery off in a RaskApp: <c>Program.cs says c.Jobs.Off()</c>.</param>
    /// <param name="wiring">The registration a hand-wired host makes: <c>AddRaskJobs&lt;AppDbContext&gt;()</c>.</param>
    /// <exception cref="InvalidOperationException">No work is in progress, or the battery is not running.</exception>
    public static T Reach<T>(string facade, string unless, string wiring)
        where T : class
    {
        var services = Services ?? throw new InvalidOperationException(
            $"{facade} was called outside any work in progress — a handler, a render, a request or a job — so "
            + $"there is no app to reach. Inject {typeof(T).Name} in the constructor there instead.");

        return services.GetService(typeof(T)) as T ?? throw new InvalidOperationException(
            $"{facade} is not running in this app. A RaskApp has it on unless {unless}; a hand-wired host calls "
            + $"builder.Services.{wiring}.");
    }

    /// <summary>Makes <paramref name="services" /> the work in progress's until the scope is disposed.</summary>
    public static ServicesScope Enter(IServiceProvider? services)
    {
        var previous = PushedServices.Value;
        PushedServices.Value = services;
        return new ServicesScope(previous);
    }

    /// <summary>Makes <paramref name="token" /> the work in progress's cancellation until the scope is disposed.</summary>
    /// <remarks>
    ///     <see cref="CancellationToken.None" /> sets the enclosing work's aside: a render entered from a handler
    ///     does it, so what it mounts is cancelled with itself and not with the handler's component.
    /// </remarks>
    public static TokenScope Enter(CancellationToken token)
    {
        var previous = PushedToken.Value;
        Push(previous, token);
        return new TokenScope(previous);
    }

    // The flow stores the token boxed, so writing the value it already holds would allocate for nothing —
    // and a render sets the token aside on every pass, nearly always with none pushed.
    private static void Push(CancellationToken current, CancellationToken token)
    {
        if (current != token)
        {
            PushedToken.Value = token;
        }
    }

    /// <summary>Restores the services that were in scope before. A struct, so entering allocates nothing.</summary>
    public readonly struct ServicesScope : IDisposable
    {
        private readonly IServiceProvider? _previous;

        internal ServicesScope(IServiceProvider? previous) => _previous = previous;

        /// <inheritdoc />
        public void Dispose() => PushedServices.Value = _previous;
    }

    /// <summary>Restores the cancellation that was in scope before. A struct, so entering allocates nothing.</summary>
    public readonly struct TokenScope : IDisposable
    {
        private readonly CancellationToken _previous;

        internal TokenScope(CancellationToken previous) => _previous = previous;

        /// <inheritdoc />
        public void Dispose() => Push(PushedToken.Value, _previous);
    }
}
