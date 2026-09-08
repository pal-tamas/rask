namespace Rask.Ui;

/// <summary>
/// The shape something is clipped to.
/// </summary>
/// <remarks>
/// Clipping only, so whatever is masked has to survive losing its corners: a photograph does, a screen
/// grab with text near the edge does not.
/// </remarks>
public enum UiMaskShape
{
    /// <summary>A circle.</summary>
    Circle = 0,

    /// <summary>A rounded square — the shape an app icon is.</summary>
    Squircle,

    /// <summary>A five-pointed star.</summary>
    Star,

    /// <summary>A fatter five-pointed star.</summary>
    Star2,

    /// <summary>A heart.</summary>
    Heart,

    /// <summary>A hexagon, points top and bottom.</summary>
    Hexagon,

    /// <summary>A hexagon, points left and right.</summary>
    Hexagon2,

    /// <summary>A pentagon.</summary>
    Pentagon,

    /// <summary>A decagon.</summary>
    Decagon,

    /// <summary>A diamond.</summary>
    Diamond,

    /// <summary>A triangle, point up.</summary>
    Triangle,

    /// <summary>A triangle, point down.</summary>
    Triangle2,

    /// <summary>A triangle, point left.</summary>
    Triangle3,

    /// <summary>A triangle, point right.</summary>
    Triangle4,

    /// <summary>The top half of whichever shape is also applied.</summary>
    Half1,

    /// <summary>The bottom half of whichever shape is also applied.</summary>
    Half2,
}
