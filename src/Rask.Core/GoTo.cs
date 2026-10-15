using Rask.Core.Routing;

namespace Rask.Core;

/// <summary>
///     A navigation <see cref="Go" /> has just made, and how it lands in the browser's history: a new page adds an entry
///     (<c>.Replacing()</c> takes the current one's place), a move of the query alone takes the current one's place
///     (<c>.Pushing()</c> adds one).
/// </summary>
public sealed class GoTo
{
    private readonly Navigator _navigator;

    internal GoTo(Navigator navigator) => _navigator = navigator;

    /// <summary>Replaces the current history entry rather than adding one, so Back skips it.</summary>
    /// <exception cref="InvalidOperationException">Called outside the event handler that navigated.</exception>
    public void Replacing() => _navigator.Replace();

    /// <summary>Adds a history entry rather than replacing the current one, so Back returns to it: <c>Go.With("page", "2").Pushing()</c>.</summary>
    /// <exception cref="InvalidOperationException">Called outside the event handler that navigated.</exception>
    public void Pushing() => _navigator.Push();
}
