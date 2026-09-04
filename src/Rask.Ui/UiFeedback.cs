namespace Rask.Ui;

/// <summary>
/// Something the page needs to say, in place.
/// </summary>
/// <remarks>
/// <c>role="alert"</c> only when the tone is <see cref="UiTone.Error" /> or <see cref="UiTone.Warning" />:
/// that role interrupts a screen reader, which is right for a failure and rude for an explanation. The
/// rest announce politely as <c>status</c>.
/// </remarks>
public sealed partial class UiAlert : Component
{
    public required string Message { get; set; }

    /// <summary><see cref="UiTone.Info" />, <see cref="UiTone.Success" />, <see cref="UiTone.Warning" /> or <see cref="UiTone.Error" />.</summary>
    public UiTone? Tone { get; set; }

    public UiVariant? Variant { get; set; }

    public UiIconName? Icon { get; set; }

    public string? Class { get; set; }

    /// <inheritdoc />
    protected override Component? Render() =>
        Div
            .Role(Tone is UiTone.Error or UiTone.Warning ? "alert" : "status")
            .Class(UiClass.Compose(
                "alert",
                Tone is { } tone ? UiClassNames.AlertTone(tone) : "",
                Variant is { } variant ? UiClassNames.AlertVariant(variant) : "",
                Class))[
            Icon is { } icon ? UiIcon.Name(icon).Class("size-5 shrink-0") : null,
            Span[Message],
            Children ?? []
        ];
}

/// <summary>
/// Work in progress, with no idea how much is left.
/// </summary>
/// <remarks>
/// The spinner is <c>aria-hidden</c> and the words beside it are what gets announced. A bare spinner tells
/// a screen reader nothing at all, and "Loading" read once is worth more than an animation.
/// </remarks>
public sealed partial class UiLoading : Component
{
    /// <summary>What is being waited for. Announced; the spinner itself is decorative.</summary>
    public new required string Text { get; set; }

    /// <summary>daisyUI's shape: <c>loading-spinner</c>, <c>loading-dots</c>, <c>loading-ring</c>…</summary>
    public string? Shape { get; set; }

    public UiSize? Size { get; set; }

    public string? Class { get; set; }

    /// <inheritdoc />
    protected override Component? Render() =>
        Span.Role("status").Class(UiClass.Compose("inline-flex items-center gap-2", Class))[
            Span
                .Class(UiClass.Compose(
                    "loading",
                    Shape ?? "loading-spinner",
                    Size is { } size ? UiClassNames.LoadingSize(size) : ""))
                .Attributes(("aria-hidden", "true")),
            Span[Text]
        ];
}

/// <summary>
/// Work in progress, with a known amount left.
/// </summary>
/// <remarks>
/// A real <c>&lt;progress&gt;</c>: it reports its own value to assistive technology, which a styled div
/// has to be told to do and usually is not.
/// </remarks>
public sealed partial class UiProgress : Component
{
    /// <summary>The accessible name — what is progressing.</summary>
    public required string Label { get; set; }

    public required double Value { get; set; }

    public double? Max { get; set; }

    public UiTone? Tone { get; set; }

    public string? Class { get; set; }

    /// <inheritdoc />
    protected override Component? Render() =>
        Progress
            .Value(Value)
            .Max(Max ?? 100)
            .Aria(new Dictionary<string, string?> { ["label"] = Label })
            .Class(UiClass.Compose(
                "progress",
                Tone is { } tone ? UiClassNames.ProgressTone(tone) : "",
                Class));
}

/// <summary>
/// A proportion drawn as a ring.
/// </summary>
/// <remarks>
/// daisyUI draws this from a CSS variable rather than from an attribute, so the percentage travels in an
/// inline <c>style</c>. <c>role="progressbar"</c> and the value attributes are set explicitly: the ring is
/// a div, and nothing about a div says what it is measuring.
/// </remarks>
public sealed partial class UiRadialProgress : Component
{
    /// <summary>The accessible name — what is progressing.</summary>
    public required string Label { get; set; }

    /// <summary>0 to 100.</summary>
    public required int Percent { get; set; }

    public string? Class { get; set; }

    /// <inheritdoc />
    protected override Component? Render()
    {
        var clamped = Math.Clamp(Percent, 0, 100);
        var text = clamped.ToString(System.Globalization.CultureInfo.InvariantCulture);

        return Div
            .Role("progressbar")
            .Class(UiClass.Compose("radial-progress", Class))
            .Style($"--value:{text}")
            .Aria(new Dictionary<string, string?>
            {
                ["label"] = Label,
                ["valuenow"] = text,
                ["valuemin"] = "0",
                ["valuemax"] = "100",
            })[$"{text}%"];
    }
}

/// <summary>
/// The shape of content that has not arrived.
/// </summary>
/// <remarks>
/// <c>aria-hidden</c>, and deliberately: a placeholder has nothing to announce, and a screen reader
/// reading out a row of empty boxes is worse than silence. Pair it with a <see cref="UiLoading" /> where
/// the wait itself needs announcing.
/// </remarks>
public sealed partial class UiSkeleton : Component
{
    public string? Class { get; set; }

    /// <inheritdoc />
    protected override Component? Render() =>
        Div.Class(UiClass.Compose("skeleton", Class)).Attributes(("aria-hidden", "true"))[Children ?? []];
}

/// <summary>
/// A hint shown on hover or focus.
/// </summary>
/// <remarks>
/// CSS-only, from daisyUI's <c>data-tip</c>. It is a hint and nothing more: a tooltip is not reachable by
/// touch and is easy to miss, so nothing that matters should live only here.
/// </remarks>
public sealed partial class UiTooltip : Component
{
    public required string Tip { get; set; }

    /// <summary>daisyUI's placement: <c>tooltip-top</c>, <c>tooltip-right</c>…</summary>
    public string? Placement { get; set; }

    /// <summary>Anything but <see cref="UiTone.Neutral" />, which daisyUI does not define for a tooltip.</summary>
    public UiTone? Tone { get; set; }

    public string? Class { get; set; }

    /// <inheritdoc />
    protected override Component? Render() =>
        Div
            .Class(UiClass.Compose(
                "tooltip",
                Placement,
                Tone is { } tone ? UiClassNames.TooltipTone(tone) : "",
                Class))
            .Attributes(("data-tip", Tip))[Children ?? []];
}
