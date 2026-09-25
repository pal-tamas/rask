namespace Rask.Wasm.Browser;

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
