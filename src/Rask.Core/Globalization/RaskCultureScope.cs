using System.Globalization;

namespace Rask.Core.Globalization;

/// <summary>
///     Carries the session's culture across a handler dispatch, where there is no render context to read
///     it from.
/// </summary>
/// <remarks>
///     Same shape as <c>DispatchServicesScope</c>, and pushed beside it. <see cref="AsyncLocal{T}" />
///     rather than <c>[ThreadStatic]</c> because a handler may await and resume on another thread.
///     <para>
///         This is the <em>second</em> of the two seams, not the primary one. The render walk does not
///         rely on it: <c>LiveRenderContext</c> re-reads the culture from the session on every walk,
///         which is what makes culture survive <c>LifecycleSyncContext</c>'s deliberate
///         <c>ExecutionContext.SuppressFlow()</c> — an <c>AsyncLocal</c> cannot cross that, and neither
///         can <c>CultureInfo.CurrentCulture</c>, which is itself an <c>AsyncLocal</c>.
///     </para>
/// </remarks>
internal static class RaskCultureScope
{
    private static readonly AsyncLocal<Entry?> _current = new();

    public static Entry? Current => _current.Value;

    public static IDisposable Push(CultureInfo culture, CultureInfo uiCulture)
    {
        var previous = _current.Value;
        _current.Value = new Entry(culture, uiCulture);
        return new Popper(previous);
    }

    /// <summary>
    ///     Pushes the culture belonging to <paramref name="services" />, or nothing at all when the app
    ///     configured no cultures.
    /// </summary>
    /// <remarks>
    ///     The <see cref="RaskCulture.IsEnabled" /> check is what keeps this free for apps that never
    ///     asked for localization: no service resolution, no allocation, no ambient write on the
    ///     dispatch path. Also pins the thread's culture for the handler's duration, for the same reason
    ///     the render walk does — code the handler calls may format through the ambient culture.
    /// </remarks>
    public static IDisposable PushFrom(IServiceProvider? services)
    {
        if (!RaskCulture.IsEnabled || services?.GetService(typeof(IRaskCulture)) is not IRaskCulture culture)
        {
            return NullScope.Instance;
        }

        return new PinnedScope(Push(culture.Culture, culture.UICulture), culture.Culture, culture.UICulture);
    }

    private sealed class NullScope : IDisposable
    {
        public static readonly NullScope Instance = new();

        public void Dispose()
        {
        }
    }

    // Pops the AsyncLocal entry and unpins the thread culture, in that order.
    private sealed class PinnedScope : IDisposable
    {
        private readonly IDisposable _inner;
        private readonly CultureInfo _culture;
        private readonly CultureInfo _previousCulture;
        private readonly CultureInfo _previousUICulture;
        private readonly CultureInfo _uiCulture;

        public PinnedScope(IDisposable inner, CultureInfo culture, CultureInfo uiCulture)
        {
            _inner = inner;
            _culture = culture;
            _uiCulture = uiCulture;
            _previousCulture = CultureInfo.CurrentCulture;
            _previousUICulture = CultureInfo.CurrentUICulture;
            CultureInfo.CurrentCulture = culture;
            CultureInfo.CurrentUICulture = uiCulture;
        }

        public void Dispose()
        {
            // Only unpin what is still ours: an awaited handler can resume on a different thread, and
            // restoring there would stamp a foreign value onto a thread this scope never touched.
            if (ReferenceEquals(CultureInfo.CurrentCulture, _culture))
            {
                CultureInfo.CurrentCulture = _previousCulture;
            }

            if (ReferenceEquals(CultureInfo.CurrentUICulture, _uiCulture))
            {
                CultureInfo.CurrentUICulture = _previousUICulture;
            }

            _inner.Dispose();
        }
    }

    internal sealed record Entry(CultureInfo Culture, CultureInfo UICulture);

    private sealed class Popper(Entry? previous) : IDisposable
    {
        public void Dispose() => _current.Value = previous;
    }
}
