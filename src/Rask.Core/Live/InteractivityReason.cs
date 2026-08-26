namespace Rask.Core.Live;

/// <summary>
///     Why a rendered page needs a live connection. A page that accumulates none can be served as a
///     plain document: no session, no socket, no runtime script.
/// </summary>
/// <remarks>
///     Flags rather than a single value because a page usually has several reasons and the first one
///     found is rarely the interesting one. It exists for diagnostics — the framework's own answer is
///     just "any reason at all" — but "why is this page interactive?" is unanswerable in production
///     without it, and an unanswerable question there makes the whole feature untunable.
/// </remarks>
[Flags]
internal enum InteractivityReason
{
    /// <summary>Nothing in the render needs a connection.</summary>
    None = 0,

    /// <summary>An element carries an event handler.</summary>
    Handler = 1 << 0,

    /// <summary>A form or bound control registered an <c>EditContext</c>.</summary>
    Form = 1 << 1,

    /// <summary>An element was given a <c>Ref</c>, which exists to be handed to JavaScript.</summary>
    ElementRef = 1 << 2,

    /// <summary>The render called into JavaScript.</summary>
    JsInterop = 1 << 3,

    /// <summary>Async lifecycle work was still in flight when the response had to go.</summary>
    QuiescenceTimeout = 1 << 4,

    /// <summary>The page, or a component in it, declared that it needs one.</summary>
    Declared = 1 << 5,
}
