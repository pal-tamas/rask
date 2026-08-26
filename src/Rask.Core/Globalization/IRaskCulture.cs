using System.Globalization;

namespace Rask.Core.Globalization;

/// <summary>
///     The visitor's culture for this session: what to format with, what language to render text in, and
///     how to change it.
/// </summary>
/// <remarks>
///     Registered by every host, whether or not the app configured any languages — an app that never
///     asked gets an instance reporting no supported cultures, so a component can take this in its
///     constructor without knowing which it is. Inject it to <em>change</em> the culture; to
///     <em>read</em> one while rendering, prefer <c>Component.Culture</c>, which also tells the render
///     cache that the component depends on it.
/// </remarks>
public interface IRaskCulture
{
    /// <summary>The culture dates, numbers and currency are formatted with.</summary>
    CultureInfo Culture { get; }

    /// <summary>The language UI text is rendered in. Usually the same as <see cref="Culture" />.</summary>
    CultureInfo UICulture { get; }

    /// <summary>The languages this app ships. Empty when culture support is off.</summary>
    IReadOnlyList<CultureInfo> Supported { get; }

    /// <summary>Whether the current culture is written right-to-left.</summary>
    bool IsRightToLeft { get; }

    /// <summary>Raised after the culture changes, so the session can re-render.</summary>
    event Action? Changed;

    /// <summary>
    ///     Switches to <paramref name="culture" /> if the app supports it, persists the choice, and
    ///     triggers a re-render. A culture that is not supported — or a runtime with no culture data —
    ///     leaves the session unchanged rather than throwing.
    /// </summary>
    /// <returns>Whether the culture changed.</returns>
    Task<bool> SetAsync(CultureInfo culture);

    /// <summary>
    ///     Switches to the named culture. Convenience for the common call site — a switcher bound to a
    ///     tag from markup or a query string — forwarding to <see cref="SetAsync(CultureInfo)" />.
    /// </summary>
    /// <returns>Whether the culture changed.</returns>
    Task<bool> SetAsync(string name);
}

/// <summary>
///     Where an explicit culture choice is stored between visits. Implemented per host: a cookie on the
///     web, nothing at all where there is nowhere to write.
/// </summary>
public interface IRaskCulturePersistence
{
    /// <summary>Stores the chosen culture.</summary>
    Task SaveAsync(string culture, string uiCulture, CancellationToken cancellationToken = default);
}
