using Rask.Core.Diagnostics;

namespace Rask.Core.Live;

/// <summary>
///     Spaces the renders a session makes after a scope for requests that scope left undrawn, once they come
///     without end, and says which components kept asking.
/// </summary>
/// <remarks>
///     <para>
///         A request that is not drawn inside the dispatch it landed in is never dropped: it is a render of its
///         own, straight after. Such a render may be asked for another while it is being made — a burst from
///         another thread does that, a few times — and a component that asks for a render from every
///         <c>Render</c> does, for ever. The two are told apart by length alone: past <see cref="Streak" />
///         renders in a row that were each asked for during the one before, each further one waits
///         <see cref="Pause" /> first. The page stays current, the session's lock is free between them for the
///         reader's events, and the server does not spin.
///     </para>
///     <para>
///         A row ends when <see cref="Quiet" /> passes between one render's end and the next one's start. The time a
///         render itself takes is not quiet: on a loaded machine one can take longer than that, and a row measured
///         end to end never reached its streak — a runaway that was never slowed or named.
///     </para>
/// </remarks>
/// <param name="clock">The session's clock.</param>
internal sealed class RenderPace(TimeProvider clock)
{
    /// <summary>How many renders in a row may each be asked for during the one before, at full speed.</summary>
    internal const int Streak = 10;

    /// <summary>How long each further one waits.</summary>
    internal static readonly TimeSpan Pause = TimeSpan.FromMilliseconds(250);

    /// <summary>How long without one ends the row.</summary>
    internal static readonly TimeSpan Quiet = TimeSpan.FromSeconds(1);

    // Said again this often while the row goes on: about once a minute at one render per Pause.
    private const int SaidAgainEvery = 240;

    // Who asked is only collected from here on, so a session that never comes near the limit pays one read.
    private const int ListenFrom = Streak - 3;

    /// <summary>What the request a scope left behind is to become.</summary>
    internal enum Turn
    {
        /// <summary>A render, at once.</summary>
        Now,

        /// <summary>A render, after <see cref="Pause" />.</summary>
        AfterAPause,

        /// <summary>Nothing: a render is already waiting out its pause, and draws this too.</summary>
        Taken,
    }

    private readonly Lock _gate = new();
    private readonly HashSet<Type> _askers = [];
    private long _lastAt;
    private long _began;
    private int _inARow;
    private bool _pausing;
    private volatile bool _listening;

    /// <summary>A pace on the system clock.</summary>
    internal RenderPace()
        : this(TimeProvider.System)
    {
    }

    /// <summary>A scope begins: a render, a dispatch or an event, whose end may leave a request behind.</summary>
    internal void Began() => Volatile.Write(ref _began, clock.GetTimestamp());

    /// <summary>Says when the render for a request a scope left behind is made.</summary>
    /// <param name="sessionId">The session, as its report names it. Null where a process has only one.</param>
    /// <param name="chained">
    ///     Whether that scope was itself such a render, so this one continues a row. One left behind by an
    ///     event's dispatch starts none: a source that ticks while the reader works is not a runaway.
    /// </param>
    internal Turn Next(string? sessionId, bool chained)
    {
        string? report = null;
        Turn turn;
        lock (_gate)
        {
            // Quiet runs from the last one's end to the start of the scope that ended now — or to now, when no scope
            // has said it began since.
            var now = clock.GetTimestamp();
            var began = Volatile.Read(ref _began);
            if (_lastAt == 0 || clock.GetElapsedTime(_lastAt, began >= _lastAt ? began : now) > Quiet)
            {
                _inARow = 0;
                _askers.Clear();
            }

            if (chained)
            {
                _lastAt = now;
                _inARow++;
                _listening = _inARow >= ListenFrom;
            }

            if (_inARow < Streak)
            {
                return Turn.Now;
            }

            if (chained && (_inARow == Streak || _inARow % SaidAgainEvery == 0))
            {
                report = Report(sessionId);
                _askers.Clear();
            }

            turn = _pausing ? Turn.Taken : Turn.AfterAPause;
            _pausing = true;
        }

        if (report is not null)
        {
            RaskDiagnostics.Report(RaskLogLevel.Warning, "Rask.Live", report);
        }

        return turn;
    }

    /// <summary>The paused render is about to be made: the next request waits out a pause of its own.</summary>
    internal void PauseOver()
    {
        lock (_gate)
        {
            _pausing = false;

            // The pause is part of the row, not the quiet that ends it.
            _lastAt = clock.GetTimestamp();
        }
    }

    /// <summary>Notes who asked for a render, while a row is long enough to be worth naming.</summary>
    internal void Asked(Component by)
    {
        if (!_listening)
        {
            return;
        }

        lock (_gate)
        {
            _askers.Add(by.GetType());
        }
    }

    private string Report(string? sessionId)
    {
        var who = _askers.Count == 0
            ? "no component's StateHasChanged"
            : string.Join(", ", _askers.Select(static type => type.Name).Order(StringComparer.Ordinal));
        return $"[Rask.LiveSession] {(sessionId is null ? "The session" : "Session " + sessionId)} has made {_inARow} renders in a row "
            + $"that were each asked for while the one before was being made. Asked by: {who}. From here each one waits "
            + $"{Pause.TotalMilliseconds:0} ms, so the page stays current and the reader's events are still answered. A "
            + "component that calls StateHasChanged from every Render does this; so does a source that raises its event "
            + "faster than the page renders.";
    }
}
