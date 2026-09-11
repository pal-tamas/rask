namespace Rask.Ui;

/// <summary>A small status pill. It IS the <c>&lt;span&gt;</c>, and what it says is its children.</summary>
/// <remarks>
/// <para>
/// daisyUI's <c>badge</c>, and <see cref="Tone" /> is a <see cref="UiTone" />. Both of those are changes,
/// and the string tone it replaced is worth recording because it failed in two ways at once.
/// </para>
/// <para>
/// It was <c>string? Tone</c> matched against four literals — <c>"danger"</c>, <c>"warn"</c>,
/// <c>"info"</c>, <c>"ok"</c> — with everything else falling through to neutral. Three call sites in the
/// showcase passed <c>"success"</c> and <c>"error"</c>, which are the names every other component in this
/// kit uses, and got a silent grey pill: the author asked for green, the reader saw neutral, and nothing
/// anywhere said so. A closed enum makes that unrepresentable.
/// </para>
/// <para>
/// The palette it hand-rolled was the second failure. <c>bg-error/10 text-error</c> reads daisyUI's error
/// SURFACE as text — 2.87:1 on daisyUI's own <c>light</c> and worse elsewhere — and because the markup
/// named none of daisyUI's own badge classes, the kit's tone corrections did not reach it either. Using
/// <c>badge badge-error</c> means it inherits the corrected fill and label with the rest of them, and
/// <c>ComponentToneContrastTests</c> covers it.
/// </para>
/// <para>
/// A <see cref="UiElement" />: <c>UiBadge.Tone(UiTone.Success)["Live"]</c>, with <c>Id</c>, <c>Data</c>
/// and the rest of the element steps from <see cref="Element" />.
/// </para>
/// </remarks>
public sealed partial class UiBadge : UiElement
{
    /// <summary>The pill's colour. Omitted, it is the theme's plain badge.</summary>
    public UiTone? Tone { get; set; }

    /// <summary>How it is filled. <see cref="UiVariant.Soft" /> is the quiet one.</summary>
    public UiVariant? Variant { get; set; }

    public UiSize? Size { get; set; }

    /// <summary>Sets it in a monospace face and lets it wrap — for a request id, a <c>key=value</c> scope.</summary>
    /// <remarks>
    /// Wrapping is the half that matters. A badge is a fixed-height pill that never breaks, which is right for
    /// "Live" and wrong for a 40-character token: one of those pushes a table wider than a phone. So a mono
    /// badge gives up the fixed height and breaks anywhere.
    /// </remarks>
    public bool? Mono { get; set; }

    /// <inheritdoc />
    protected override string TagName => "span";

    /// <inheritdoc />
    protected override string? ResolveClass() =>
        UiClass.Compose(
            "badge",
            Tone is { } tone ? UiClassNames.BadgeTone(tone) : "",
            Variant is { } variant ? UiClassNames.BadgeVariant(variant) : "",
            Size is { } size ? UiClassNames.BadgeSize(size) : "",
            Mono is true ? "h-auto max-w-full break-all font-mono" : "",
            Class);
}
