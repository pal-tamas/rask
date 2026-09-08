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

/// <summary>
/// A glow around something worth looking at.
/// </summary>
/// <remarks>
/// Decoration, and it says nothing — a reader who cannot see it loses nothing, so it carries no role and
/// no label. Use it on the one thing a surface is steering towards (the recommended plan, the primary
/// card) and not on several, since a page where everything glows has singled out nothing.
/// </remarks>
public sealed partial class UiAura : Component
{
    /// <summary>Which glow. Omitted, it is daisyUI's plain one.</summary>
    public UiAuraStyle? Style { get; set; }

    /// <summary>How far the glow reaches.</summary>
    public UiSize? Size { get; set; }

    public string? Class { get; set; }

    /// <inheritdoc />
    protected override Component? Render() =>
        Div.Class(UiClass.Compose(
            "aura",
            Style is { } style ? UiClassNames.AuraStyle(style) : "",
            Size is { } size ? UiClassNames.AuraSize(size) : "",
            Class))[
            Children ?? []
        ];
}

/// <summary>
/// A card that tilts towards the pointer.
/// </summary>
/// <remarks>
/// Pointer-only by construction — there is no hover on a touch screen and none from a keyboard — so
/// nothing may depend on the tilt. It is an effect on content that is already complete without it.
/// </remarks>
public sealed partial class UiHover3d : Component
{
    public string? Class { get; set; }

    /// <inheritdoc />
    protected override Component? Render() =>
        Div.Class(UiClass.Compose("hover-3d", Class))[Children ?? []];
}

/// <summary>
/// Several images in the space of one, each revealed by hovering its column.
/// </summary>
/// <remarks>
/// <para>
/// The first child is what shows at rest and the rest are stacked behind it, so put the image that has
/// to work on its own first: on a touch screen, that is the only one anybody sees. daisyUI lays out up
/// to nine and ignores the rest.
/// </para>
/// <para>
/// Its children are stretched to the container's height, so the container needs one — give it a height
/// or an aspect ratio through <see cref="Class" />, or it collapses to nothing.
/// </para>
/// </remarks>
public sealed partial class UiHoverGallery : Component
{
    public string? Class { get; set; }

    /// <inheritdoc />
    protected override Component? Render() =>
        // <figure>, which is what a run of images with no individual caption is. daisyUI styles the
        // class on any element and specifically handles the figure case.
        Figure.Class(UiClass.Compose("hover-gallery", Class))[Children ?? []];
}

/// <summary>
/// One slot of text, cycling through several words.
/// </summary>
/// <remarks>
/// <para>
/// <b>The speed is a Tailwind duration utility, not a property here</b>, and that is a constraint rather
/// than a preference. daisyUI reads the cycle length from <c>--tw-duration</c>, which is set by a
/// <c>duration-*</c> class; a <c>TimeSpan</c> property would have to turn a number into a class name at
/// run time, and a name built that way is invisible to Tailwind's scan, absent from the sheet, and
/// silently ignored. Pass <c>duration-[3s]</c> (or any <c>duration-*</c>) through <see cref="Class" />
/// and it will be in the sheet because it is written down. Unset, daisyUI uses ten seconds.
/// </para>
/// <para>
/// All the words are in the markup, so a reader who never sees the animation reads the list. That also
/// means the phrase has to make sense with every one of them: this rotates a word, it does not rewrite
/// a sentence.
/// </para>
/// <para>
/// It respects <c>prefers-reduced-motion</c> — daisyUI steps between the words rather than sliding —
/// which is the browser's, not something a call site has to remember.
/// </para>
/// </remarks>
public sealed partial class UiTextRotate : Component
{
    /// <summary>The words to cycle. daisyUI lays out up to eight.</summary>
    public required IReadOnlyList<string> Words { get; set; }

    public string? Class { get; set; }

    /// <inheritdoc />
    protected override Component? Render() =>
        Span.Class(UiClass.Compose("text-rotate", Class))[
            // The inner wrapper is daisyUI's `> *`: it becomes the grid that scrolls, and counts ITS
            // children to pick the animation. Flattening this away leaves the words unanimated.
            Span[
                Words.Select(word => Span.Key(word)[word])
            ]
        ];
}

/// <summary>
/// A stack of sections where opening one closes the others.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Open" /> is the key of the section showing, and <c>null</c> is all of them closed — so the
/// page owns which one it is and can open a section in response to something that happened elsewhere.
/// That is the difference from a run of <see cref="UiCollapse" /> sharing a <c>Group</c>, which the
/// browser mutually excludes without telling anyone which one won.
/// </para>
/// <para>
/// Each section needs a <c>Key</c>, and it is the identity <see cref="Open" /> names as well as the one
/// reconciliation uses.
/// </para>
/// </remarks>
public sealed partial class UiAccordion : Component
{
    /// <summary>The key of the open section, or <c>null</c> for none.</summary>
    public string? Open { get; set; }

    /// <summary>Runs with the key the reader asked to open, or <c>null</c> if they closed the open one.</summary>
    public Action<string?>? OnOpen { get; set; }

    public string? Class { get; set; }

    /// <inheritdoc />
    protected override Component? Render() =>
        Div.Class(UiClass.Compose("join join-vertical w-full", Class))[
            // Each section reads Open and OnOpen off the accordion through context rather than being
            // handed them: a section is written by the CALLER, inside the accordion's children, so
            // there is no call site at which to pass them down.
            Context.Provide(new UiAccordionState(Open, OnOpen))[Children ?? []]
        ];
}

/// <summary>What an accordion tells its sections. Not a call site's concern.</summary>
/// <param name="Open">The key of the open section.</param>
/// <param name="OnOpen">What to call when a section is asked to open or close.</param>
// Qualified rather than imported: `using System.ComponentModel` puts a SECOND `Component` in scope and
// every `Component` in this file becomes CS0104-ambiguous.
[System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
public sealed record UiAccordionState(string? Open, Action<string?>? OnOpen);

/// <summary>
/// One titled section of a <see cref="UiAccordion" />.
/// </summary>
/// <remarks>
/// Its <c>Key</c> is its identity in the accordion, so a section without one cannot be opened. It reads
/// the open state from the accordion above it and refuses to render outside one, rather than drawing a
/// section that silently never opens.
/// </remarks>
public sealed partial class UiAccordionSection : Component
{
    /// <summary>The heading, and the thing you press.</summary>
    public new required string Title { get; set; }

    /// <summary>Draws the arrow or plus marker.</summary>
    public UiMarker? Marker { get; set; }

    public string? Class { get; set; }

    /// <inheritdoc />
    protected override Component? Render()
    {
        // Not Context.Required, whose message would name Context.Provide<UiAccordionState> — machinery
        // nobody writes. The mistake this catches is a section outside an accordion, and the sentence a
        // reader needs names the two components they typed.
        var state = Context.Get<UiAccordionState>()
            ?? throw new InvalidOperationException(
                $"UiAccordionSection \"{Title}\" is not inside a UiAccordion. Put it in one: "
                + "UiAccordion.Open(key).OnOpen(k => …)[ UiAccordionSection.Key(\"a\").Title(\"…\")[ … ] ].");

        var key = Key as string;
        var open = key is not null && state.Open == key;

        var title = Button
            .Type("button")
            .Class("collapse-title flex w-full items-center text-left font-semibold")
            .Aria(new Dictionary<string, string?> { ["expanded"] = open ? "true" : "false" });

        if (state.OnOpen is { } onOpen)
        {
            title = title.OnClick(() => onOpen(open ? null : key));
        }

        return Div.Class(UiClass.Compose(
            "collapse join-item border border-base-300 bg-base-100",
            Marker is { } marker ? UiClassNames.Marker(marker) : "",
            // Both, for the same reason UiDropdown writes dropdown-close: `collapse` opens on
            // :focus-within too, so omitting collapse-open is not the same as being closed.
            open ? "collapse-open" : "collapse-close",
            Class))[
            title[Title],
            Div.Class("collapse-content text-sm")[Children ?? []]
        ];
    }
}
