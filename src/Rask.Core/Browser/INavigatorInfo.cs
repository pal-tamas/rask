namespace Rask.Core.Browser;

/// <summary>
///     Read-only facts about the browser environment, from <c>window.navigator</c>
///     (<see href="https://developer.mozilla.org/en-US/docs/Web/API/Navigator" />). Inject it through a
///     component constructor and read from a lifecycle hook or event handler.
/// </summary>
/// <remarks>
///     These are JavaScript <em>properties</em>, not methods; the client resolves the dotted identifier
///     and returns the value directly when the last segment isn't a function, so no JS helper is needed.
///     <see cref="OnLineAsync" /> is the seed for offline detection in a later PWA step.
/// </remarks>
public interface INavigatorInfo
{
    /// <summary>
    ///     Whether the browser believes it currently has network connectivity (<c>navigator.onLine</c>).
    ///     A <c>true</c> result only means "not definitely offline" — it is not a reachability guarantee.
    /// </summary>
    ValueTask<bool> OnLineAsync();

    /// <summary>The user's preferred UI language, e.g. <c>"en-US"</c> (<c>navigator.language</c>).</summary>
    ValueTask<string> LanguageAsync();

    /// <summary>The browser's user-agent string (<c>navigator.userAgent</c>).</summary>
    ValueTask<string> UserAgentAsync();
}
