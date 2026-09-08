namespace Rask.Ui;

/// <summary>
/// A link, in the theme's colours.
/// </summary>
/// <remarks>
/// <c>link-hover</c> is the default rather than an option: a link that is only distinguishable by colour
/// fails for readers who cannot see the difference, and an underline on hover alone is not enough — the
/// surrounding text has to be what tells them. Set <see cref="Underline" /> to false only where something
/// else already marks it as a link.
/// </remarks>
public sealed partial class UiLink : Component
{
    public new required string Text { get; set; }

    public required string Href { get; set; }

    public UiTone? Tone { get; set; }

    /// <summary>Underlines on hover. Default true.</summary>
    public bool? Underline { get; set; }

    public string? Class { get; set; }

    /// <inheritdoc />
    protected override Component? Render() =>
        A.Href(Href).Class(UiClass.Compose(
            "link",
            Underline == false ? "" : "link-hover",
            Tone is { } tone ? UiClassNames.LinkTone(tone) : "",
            Class))[Text];
}
