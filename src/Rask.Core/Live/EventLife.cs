using Microsoft.Extensions.DependencyInjection;

namespace Rask.Core.Live;

/// <summary>
///     One component's part of a live event — or the event's own, for what no component runs — and the work it
///     started: its scope, opened on first use, lives until the event has ended and every lifecycle hook and read of
///     this part is done. Guarded by <see cref="EventServices" />' lock.
/// </summary>
internal sealed class EventLife(EventServices services)
{
    /// <summary>The session's event services this part belongs to: what a flow carrying it is checked against.</summary>
    internal EventServices Services => services;

    /// <summary>This part's own services, or null while it has resolved nothing of the app's.</summary>
    internal AsyncServiceScope? Scope { get; set; }

    /// <summary>The hooks and reads of this part that are still running.</summary>
    internal int Holds { get; set; }

    /// <summary>Whether the event itself is over: its last render has been sent.</summary>
    internal bool Ended { get; set; }

    /// <summary>Whether nothing needs the scope any more.</summary>
    internal bool Done => Ended && Holds == 0;
}
