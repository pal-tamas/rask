namespace Rask.Ui;

/// <summary>
/// How a component is DRAWN, independently of what colour it is.
/// </summary>
/// <remarks>
/// <para>
/// daisyUI separates the two: <c>btn-primary</c> says which colour, <c>btn-outline</c> says how it is
/// filled, and they compose. Keeping them apart in the API is what lets a caller ask for an outlined
/// error button without the kit having to enumerate every pairing as its own member.
/// </para>
/// <para>
/// Components that have no outlined or ghosted form ignore this and draw their default.
/// </para>
/// </remarks>
public enum UiVariant
{
    /// <summary>Filled with its colour. The default.</summary>
    Solid = 0,

    /// <summary>Its colour as a border and text, over nothing.</summary>
    Outline,

    /// <summary>Its colour heavily diluted into the surface behind it.</summary>
    Soft,

    /// <summary>Outlined, with a dashed border. For the not-yet-real.</summary>
    Dash,

    /// <summary>No fill and no border until hovered. What a quiet action looks like.</summary>
    Ghost,

    /// <summary>Drawn as a hyperlink.</summary>
    Link,
}
