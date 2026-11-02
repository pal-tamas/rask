namespace Rask;

/// <summary>
///     A picture that is part of the kit's chrome — an avatar, a brand's logo, a flag — rather than the page's content.
/// </summary>
/// <remarks>
///     A press on one is a press on its control, so it does not start a drag of the image. WebKit and Blink read
///     ui.css's <c>-webkit-user-drag: none</c> (NATIVE FEEL); Firefox reads only the attribute, which is why the
///     element carries it. A link around the picture still drags as a link.
/// </remarks>
internal static class UiChromeImage
{
    /// <summary>The attribute, for <c>Attributes(…)</c>: <c>draggable="false"</c>.</summary>
    internal static readonly (string Name, string? Value)[] NotDragged = [("draggable", "false")];
}
