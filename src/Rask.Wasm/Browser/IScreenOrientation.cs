using Microsoft.JSInterop;

namespace Rask.Wasm.Browser;

/// <summary>The current screen orientation (<c>ScreenOrientation.type</c>).</summary>
public enum OrientationType
{
    /// <summary>Unrecognised value — the browser reported a type Rask doesn't model.</summary>
    Unknown,

    /// <summary><c>portrait-primary</c> — upright portrait.</summary>
    PortraitPrimary,

    /// <summary><c>portrait-secondary</c> — upside-down portrait.</summary>
    PortraitSecondary,

    /// <summary><c>landscape-primary</c> — landscape, device rotated clockwise.</summary>
    LandscapePrimary,

    /// <summary><c>landscape-secondary</c> — landscape, device rotated counter-clockwise.</summary>
    LandscapeSecondary
}

/// <summary>An orientation a screen can be locked to (<c>ScreenOrientation.lock()</c>).</summary>
public enum OrientationLock
{
    /// <summary><c>any</c> — any orientation the device allows.</summary>
    Any,

    /// <summary><c>natural</c> — the device's natural orientation.</summary>
    Natural,

    /// <summary><c>portrait</c> — either portrait orientation.</summary>
    Portrait,

    /// <summary><c>landscape</c> — either landscape orientation.</summary>
    Landscape,

    /// <summary><c>portrait-primary</c>.</summary>
    PortraitPrimary,

    /// <summary><c>portrait-secondary</c>.</summary>
    PortraitSecondary,

    /// <summary><c>landscape-primary</c>.</summary>
    LandscapePrimary,

    /// <summary><c>landscape-secondary</c>.</summary>
    LandscapeSecondary
}

/// <summary>A reading of the current screen orientation.</summary>
/// <param name="Type">The orientation type.</param>
/// <param name="Angle">The clockwise angle in degrees relative to the natural orientation (0/90/180/270).</param>
public sealed record OrientationInfo(OrientationType Type, int Angle);

// Wire shape for __raskOrientation.get — the browser's hyphenated string plus angle, mapped to the
// typed OrientationInfo in C#. Registered for trim-safe source-gen in RaskWasmBrowserJsonContext.
internal sealed record OrientationReading(string? Type, int Angle);

/// <summary>
///     Typed access to the Screen Orientation API
///     (<see href="https://developer.mozilla.org/en-US/docs/Web/API/Screen_Orientation_API" />) — read the
///     current orientation and, for an installed/fullscreen app, lock it. <b>WASM-only:</b> locking needs
///     the live document (and usually fullscreen), state the Server/WebSocket transport can't carry, so
///     it's registered only by the WASM host.
/// </summary>
/// <remarks>
///     Requires a secure context. <see cref="LockAsync" /> rejects unless the document is fullscreen on
///     most browsers, and on desktop it's frequently unsupported — gate on <see cref="IsSupportedAsync" />
///     and wrap in try/catch; a rejection surfaces as a <see cref="JSException" />.
/// </remarks>
public interface IScreenOrientation
{
    /// <summary>Whether the browser exposes screen orientation (<c>"orientation" in screen</c>).</summary>
    ValueTask<bool> IsSupportedAsync();

    /// <summary>Reads the current orientation type and angle (<c>screen.orientation</c>).</summary>
    ValueTask<OrientationInfo> GetAsync();

    /// <summary>
    ///     Locks the screen to <paramref name="orientation" /> (<c>screen.orientation.lock</c>). Usually
    ///     requires fullscreen; rejects with a <see cref="JSException" /> when not permitted.
    /// </summary>
    ValueTask LockAsync(OrientationLock orientation);

    /// <summary>Releases any orientation lock (<c>screen.orientation.unlock</c>).</summary>
    ValueTask UnlockAsync();
}

/// <summary>
///     Default <see cref="IScreenOrientation" />, backed by the unified <see cref="IJSRuntime" />. Reads go
///     through the framework's <c>__raskOrientation</c> helper, which returns the orientation as a plain
///     <c>{ type, angle }</c> object; the hyphenated type string is mapped to <see cref="OrientationType" />
///     in C#.
/// </summary>
public sealed class ScreenOrientation(IJSRuntime js) : IScreenOrientation
{
    /// <inheritdoc />
    public ValueTask<bool> IsSupportedAsync() => js.InvokeAsync<bool>("__raskOrientation.isSupported");

    /// <inheritdoc />
    public async ValueTask<OrientationInfo> GetAsync()
    {
        var reading = await js.InvokeAsync<OrientationReading>("__raskOrientation.get");
        return new OrientationInfo(MapType(reading.Type), reading.Angle);
    }

    /// <inheritdoc />
    public ValueTask LockAsync(OrientationLock orientation) =>
        js.InvokeVoidAsync("__raskOrientation.lock", ToSpecName(orientation));

    /// <inheritdoc />
    public ValueTask UnlockAsync() => js.InvokeVoidAsync("__raskOrientation.unlock");

    private static OrientationType MapType(string? type) => type switch
    {
        "portrait-primary" => OrientationType.PortraitPrimary,
        "portrait-secondary" => OrientationType.PortraitSecondary,
        "landscape-primary" => OrientationType.LandscapePrimary,
        "landscape-secondary" => OrientationType.LandscapeSecondary,
        _ => OrientationType.Unknown
    };

    // The Screen Orientation API uses hyphenated lowercase lock names.
    private static string ToSpecName(OrientationLock orientation) => orientation switch
    {
        OrientationLock.Any => "any",
        OrientationLock.Natural => "natural",
        OrientationLock.Portrait => "portrait",
        OrientationLock.Landscape => "landscape",
        OrientationLock.PortraitPrimary => "portrait-primary",
        OrientationLock.PortraitSecondary => "portrait-secondary",
        OrientationLock.LandscapePrimary => "landscape-primary",
        OrientationLock.LandscapeSecondary => "landscape-secondary",
        _ => throw new ArgumentOutOfRangeException(nameof(orientation), orientation, "Unknown orientation lock.")
    };
}
