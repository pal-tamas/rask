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

    /// <summary>The services of the work in progress, or null outside any.</summary>
    public static IServiceProvider? Services => PushedServices.Value ?? FallbackServices?.Invoke();

    /// <summary>The cancellation of the work in progress, or <see cref="CancellationToken.None" /> outside any.</summary>
    public static CancellationToken CancellationToken => PushedToken.Value;

    /// <summary><paramref name="token" /> when the caller passed one, else the work in progress's.</summary>
    public static CancellationToken Or(CancellationToken token) =>
        token.CanBeCanceled ? token : PushedToken.Value;

    /// <summary>Makes <paramref name="services" /> the work in progress's until the scope is disposed.</summary>
    public static ServicesScope Enter(IServiceProvider? services)
    {
        var previous = PushedServices.Value;
        PushedServices.Value = services;
        return new ServicesScope(previous);
    }

    /// <summary>Makes <paramref name="token" /> the work in progress's cancellation until the scope is disposed.</summary>
    public static TokenScope Enter(CancellationToken token)
    {
        var previous = PushedToken.Value;
        PushedToken.Value = token;
        return new TokenScope(previous);
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
        public void Dispose() => PushedToken.Value = _previous;
    }
}
