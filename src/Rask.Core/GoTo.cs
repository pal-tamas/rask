using Rask.Core.Routing;

namespace Rask.Core;

/// <summary>A navigation <see cref="Go.To(string)" /> has just made, and how it lands in the browser's history.</summary>
public sealed class GoTo
{
    private readonly Navigator _navigator;

    internal GoTo(Navigator navigator) => _navigator = navigator;

    /// <summary>Replaces the current history entry rather than adding one, so Back skips it.</summary>
    /// <exception cref="InvalidOperationException">Called outside the event handler that navigated.</exception>
    public void Replacing() => _navigator.Replace();
}
