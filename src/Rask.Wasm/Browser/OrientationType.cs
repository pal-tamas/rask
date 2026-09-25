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
