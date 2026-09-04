namespace Rask.Ui;

/// <summary>
/// A message in a conversation.
/// </summary>
/// <remarks>
/// <see cref="Mine" /> chooses the side. daisyUI has no notion of who is speaking, only of left and
/// right, so the component takes the meaningful question and answers the presentational one itself.
/// </remarks>
public sealed partial class UiChatBubble : Component
{
    public required string Message { get; set; }

    /// <summary>Who said it. Rendered above the bubble.</summary>
    public string? Author { get; set; }

    /// <summary>When. Rendered beside the author.</summary>
    public string? When { get; set; }

    /// <summary>Puts it on the trailing side, as the reader's own message.</summary>
    public bool? Mine { get; set; }

    public UiTone? Tone { get; set; }

    public string? Class { get; set; }

    /// <inheritdoc />
    protected override Component? Render() =>
        Div.Class(UiClass.Compose(Mine == true ? "chat chat-end" : "chat chat-start", Class))[
            Author is null && When is null
                ? null
                : Div.Class("chat-header")[
                    Author is { } author ? Span[author] : null,
                    When is { } when ? Time.Class("ms-1 text-xs opacity-50")[when] : null
                ],
            Div.Class(UiClass.Compose(
                "chat-bubble",
                Tone is { } tone ? ToneClass(tone) : ""))[Message]
        ];

    // A literal per tone, like every other class the kit writes: daisyUI emits a component's CSS only
    // where the scanner can see the whole name.
    private static string ToneClass(UiTone tone) => tone switch
    {
        UiTone.Primary => "chat-bubble-primary",
        UiTone.Secondary => "chat-bubble-secondary",
        UiTone.Accent => "chat-bubble-accent",
        UiTone.Info => "chat-bubble-info",
        UiTone.Success => "chat-bubble-success",
        UiTone.Warning => "chat-bubble-warning",
        UiTone.Error => "chat-bubble-error",
        UiTone.Neutral => "chat-bubble-neutral",
        _ => "",
    };
}

/// <summary>
/// Two versions of something, with a handle to wipe between them.
/// </summary>
/// <remarks>
/// The handle is a <c>tabindex</c>-bearing div that daisyUI drives from focus and pointer position in
/// CSS, so the comparison works with no script. It is a visual comparison and nothing more: give both
/// sides real alternative text, because the difference itself is not announced.
/// </remarks>
public sealed partial class UiDiff : Component
{
    public required Component Before { get; set; }

    public required Component After { get; set; }

    /// <summary>The accessible name for the handle.</summary>
    public string? HandleLabel { get; set; }

    public string? Class { get; set; }

    /// <inheritdoc />
    protected override Component? Render() =>
        Figure.Class(UiClass.Compose("diff aspect-16/9", Class))[
            Div.Class("diff-item-1")[Before],
            Div.Class("diff-item-2")[After],
            Div
                .Class("diff-resizer")
                .Attributes(("tabindex", "0"))
                .Aria(new Dictionary<string, string?> { ["label"] = HandleLabel ?? "Compare" })
        ];
}

/// <summary>
/// Rows of information, each a small grid.
/// </summary>
public sealed partial class UiList : Component
{
    public string? Class { get; set; }

    /// <inheritdoc />
    protected override Component? Render() =>
        Ul.Class(UiClass.Compose("list rounded-box bg-base-100", Class))[Children ?? []];
}

/// <summary>
/// One row of a <see cref="UiList" />.
/// </summary>
/// <remarks>
/// The child marked <c>list-col-grow</c> is the one that takes the remaining width; daisyUI gives every
/// other child its intrinsic size. Set <see cref="Grow" /> on the part that should stretch, which is
/// almost always the text rather than the picture beside it.
/// </remarks>
public sealed partial class UiListRow : Component
{
    /// <summary>The part that takes the remaining width.</summary>
    public required Component Grow { get; set; }

    /// <summary>Before the growing part — a picture, an icon, an index.</summary>
    public Component? Leading { get; set; }

    /// <summary>After it — usually the actions.</summary>
    public Component? Trailing { get; set; }

    public string? Class { get; set; }

    /// <inheritdoc />
    protected override Component? Render() =>
        Li.Class(UiClass.Compose("list-row", Class))[
            Leading,
            Div.Class("list-col-grow")[Grow],
            Trailing
        ];
}

/// <summary>
/// A number that animates as it changes.
/// </summary>
/// <remarks>
/// <para>
/// daisyUI animates the digits from a CSS variable, so the value travels in an inline <c>style</c> rather
/// than as text — the text inside is what a reader without CSS sees and what a screen reader announces,
/// so both are rendered.
/// </para>
/// <para>
/// It does not count down on its own. Nothing here runs a timer, because the kit ships no JavaScript;
/// the owning page re-renders it with a new value, and daisyUI animates the transition.
/// </para>
/// </remarks>
public sealed partial class UiCountdown : Component
{
    public required int Value { get; set; }

    /// <summary>The accessible name — what is being counted.</summary>
    public required string Label { get; set; }

    public string? Class { get; set; }

    /// <inheritdoc />
    protected override Component? Render()
    {
        var text = Value.ToString(System.Globalization.CultureInfo.InvariantCulture);

        return Span
            .Class(UiClass.Compose("countdown", Class))
            .Aria(new Dictionary<string, string?> { ["label"] = Label })[
            Span.Style($"--value:{text}").Attributes(("aria-hidden", "true"))[text]
        ];
    }
}

/// <summary>
/// A browser window around a picture of a page.
/// </summary>
public sealed partial class UiMockupBrowser : Component
{
    /// <summary>The address shown in the bar.</summary>
    public string? Url { get; set; }

    public string? Class { get; set; }

    /// <inheritdoc />
    protected override Component? Render() =>
        Div.Class(UiClass.Compose("mockup-browser border border-base-300 bg-base-100", Class))[
            Div.Class("mockup-browser-toolbar")[
                Url is { } url ? Div.Class("input")[url] : null
            ],
            Div.Class("border-t border-base-300")[Children ?? []]
        ];
}

/// <summary>
/// A terminal, for showing a command.
/// </summary>
/// <remarks>
/// Each line is a <c>&lt;pre&gt;</c> with a <c>data-prefix</c>, which is how daisyUI draws the prompt
/// character — it is a CSS pseudo-element, so it is decoration and is never copied when a reader selects
/// the command. That is the point: a prompt that comes along with the copied text is a broken paste.
/// </remarks>
public sealed partial class UiMockupCode : Component
{
    /// <summary>The lines, each with the prefix daisyUI draws before it ("$", ">", "1").</summary>
    public required IReadOnlyList<(string Prefix, string Text)> Lines { get; set; }

    public string? Class { get; set; }

    /// <inheritdoc />
    protected override Component? Render() =>
        Div.Class(UiClass.Compose("mockup-code", Class))[
            Lines.Select((line, index) =>
                Pre.Key(index).Attributes(("data-prefix", line.Prefix))[Code[line.Text]])
        ];
}

/// <summary>
/// A phone around a picture of a screen.
/// </summary>
public sealed partial class UiMockupPhone : Component
{
    public string? Class { get; set; }

    /// <inheritdoc />
    protected override Component? Render() =>
        Div.Class(UiClass.Compose("mockup-phone", Class))[
            Div.Class("mockup-phone-camera"),
            Div.Class("mockup-phone-display")[Children ?? []]
        ];
}

/// <summary>
/// A plain window frame.
/// </summary>
public sealed partial class UiMockupWindow : Component
{
    public string? Class { get; set; }

    /// <inheritdoc />
    protected override Component? Render() =>
        Div.Class(UiClass.Compose("mockup-window border border-base-300 bg-base-100", Class))[
            Div.Class("border-t border-base-300")[Children ?? []]
        ];
}
