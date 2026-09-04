namespace Rask.Ui;

/// <summary>
/// A line between two parts of a page, optionally with a word on it.
/// </summary>
public sealed partial class UiDivider : Component
{
    /// <summary>The word on the line — "or", "then". Omitted, it is a plain rule.</summary>
    public new string? Text { get; set; }

    public UiTone? Tone { get; set; }

    /// <summary>Runs down instead of across. daisyUI's <c>divider-horizontal</c>.</summary>
    public bool? Vertical { get; set; }

    public string? Class { get; set; }

    /// <inheritdoc />
    protected override Component? Render() =>
        Div.Class(UiClass.Compose(
            "divider",
            Vertical == true ? "divider-horizontal" : "",
            Tone is { } tone ? UiClassNames.DividerTone(tone) : "",
            Class))[Text];
}

/// <summary>
/// A page with a panel that slides in beside it.
/// </summary>
/// <remarks>
/// <para>
/// The open state is a hidden checkbox and the sliding is CSS, so this works with no script: the button
/// that opens it is a <c>&lt;label&gt;</c> pointing at the checkbox, which is why <see cref="Id" /> is
/// required and has to be unique on the page.
/// </para>
/// <para>
/// The overlay is a label too, so a click outside closes it — the one dismissal a
/// <see cref="UiDropdown" /> cannot offer without script. Pair it with <c>lg:drawer-open</c> in
/// <see cref="Class" /> to have the panel simply be there on a wide screen.
/// </para>
/// </remarks>
public sealed partial class UiDrawer : Component
{
    /// <summary>Joins the toggle, the overlay and the panel. Must be unique on the page.</summary>
    public required string Id { get; set; }

    /// <summary>The panel's contents. The page itself is this component's children.</summary>
    public required Component Side { get; set; }

    /// <summary>The accessible name for the closing overlay.</summary>
    public string? CloseLabel { get; set; }

    public string? Class { get; set; }

    /// <inheritdoc />
    protected override Component? Render() =>
        Div.Class(UiClass.Compose("drawer", Class))[
            Input.Value(false).Id(Id).Class("drawer-toggle"),
            Div.Class("drawer-content")[Children ?? []],
            Div.Class("drawer-side")[
                Label
                    .For(Id)
                    .Class("drawer-overlay")
                    .Aria(new Dictionary<string, string?> { ["label"] = CloseLabel ?? "Close" }),
                Side
            ]
        ];
}

/// <summary>
/// The foot of a page.
/// </summary>
public sealed partial class UiFooter : Component
{
    /// <summary>Lays the columns out in a row rather than stacked.</summary>
    public bool? Horizontal { get; set; }

    public string? Class { get; set; }

    /// <inheritdoc />
    protected override Component? Render() =>
        Footer.Class(UiClass.Compose(
            "footer",
            Horizontal == true ? "footer-horizontal" : "",
            "bg-base-200 p-10",
            Class))[Children ?? []];
}

/// <summary>
/// A full-width banner with its content centred.
/// </summary>
public sealed partial class UiHero : Component
{
    public string? Class { get; set; }

    /// <inheritdoc />
    protected override Component? Render() =>
        Div.Class(UiClass.Compose("hero", Class))[
            Div.Class("hero-content text-center")[Children ?? []]
        ];
}

/// <summary>
/// A badge pinned to the corner of something.
/// </summary>
/// <remarks>
/// The badge goes in <see cref="Badge" /> and the thing it marks is the children. Two slots rather than
/// one, because daisyUI needs the badge to carry <c>indicator-item</c> and the caller should not have to
/// remember which of two children gets it.
/// </remarks>
public sealed partial class UiIndicator : Component
{
    public required Component Badge { get; set; }

    public string? Class { get; set; }

    /// <inheritdoc />
    protected override Component? Render() =>
        Div.Class(UiClass.Compose("indicator", Class))[
            Span.Class("indicator-item")[Badge],
            Children ?? []
        ];
}

/// <summary>
/// Controls joined into one continuous group.
/// </summary>
/// <remarks>
/// Each child needs <c>join-item</c> to lose its own outer corners; daisyUI cannot add it from the
/// container, so a child that looks detached is usually missing it.
/// </remarks>
public sealed partial class UiJoin : Component
{
    /// <summary>Stacks the items instead of running them across.</summary>
    public bool? Vertical { get; set; }

    public string? Class { get; set; }

    /// <inheritdoc />
    protected override Component? Render() =>
        Div.Class(UiClass.Compose(
            "join",
            Vertical == true ? "join-vertical" : "join-horizontal",
            Class))[Children ?? []];
}

/// <summary>
/// Elements stacked on top of one another.
/// </summary>
public sealed partial class UiStack : Component
{
    public string? Class { get; set; }

    /// <inheritdoc />
    protected override Component? Render() =>
        Div.Class(UiClass.Compose("stack", Class))[Children ?? []];
}

/// <summary>
/// A person or a thing, as a picture.
/// </summary>
/// <remarks>
/// <see cref="Alt" /> is required. An avatar with no alternative text is announced as its file name or as
/// nothing at all, and it is usually the only thing identifying a row.
/// </remarks>
public sealed partial class UiAvatar : Component
{
    public required string Src { get; set; }

    public required string Alt { get; set; }

    /// <summary>Tailwind sizing for the frame, for example <c>w-12</c>. Defaults to <c>w-10</c>.</summary>
    public string? Size { get; set; }

    /// <summary>Rounds it fully. daisyUI's own examples use <c>rounded-full</c> on the frame.</summary>
    public bool? Round { get; set; }

    public string? Class { get; set; }

    /// <inheritdoc />
    protected override Component? Render() =>
        Div.Class(UiClass.Compose("avatar", Class))[
            Div.Class(UiClass.Compose(Size ?? "w-10", Round == false ? "rounded" : "rounded-full"))[
                Img.Src(Src).Alt(Alt)
            ]
        ];
}

/// <summary>
/// A key on a keyboard.
/// </summary>
public sealed partial class UiKbd : Component
{
    public new required string Text { get; set; }

    public UiSize? Size { get; set; }

    public string? Class { get; set; }

    /// <inheritdoc />
    protected override Component? Render() =>
        Kbd.Class(UiClass.Compose(
            "kbd",
            Size is { } size ? UiClassNames.KbdSize(size) : "",
            Class))[Text];
}

/// <summary>
/// Events in order.
/// </summary>
public sealed partial class UiTimeline : Component
{
    /// <summary>Runs down the page rather than across it.</summary>
    public bool? Vertical { get; set; }

    public string? Class { get; set; }

    /// <inheritdoc />
    protected override Component? Render() =>
        Ul.Class(UiClass.Compose(
            "timeline",
            Vertical == true ? "timeline-vertical" : "timeline-horizontal",
            Class))[Children ?? []];
}

/// <summary>
/// A row that scrolls sideways, one item at a time.
/// </summary>
/// <remarks>
/// CSS scroll-snap, so the swiping is the browser's — no script, and it keeps the momentum and the
/// scrollbar a native scroller has. Each child should carry <c>carousel-item</c>.
/// </remarks>
public sealed partial class UiCarousel : Component
{
    public string? Class { get; set; }

    /// <inheritdoc />
    protected override Component? Render() =>
        Div.Class(UiClass.Compose("carousel", Class))[Children ?? []];
}

/// <summary>
/// A rating, as stars.
/// </summary>
/// <remarks>
/// <para>
/// Radio inputs sharing a name, which is what makes it settable with no script and reachable by keyboard.
/// </para>
/// <para>
/// The first radio is hidden and represents "no rating": without it a rating can be raised and lowered but
/// never cleared, because a radio group offers no way back to none.
/// </para>
/// </remarks>
public sealed partial class UiRating : Component
{
    /// <summary>The name the radios share. Must be unique on the page.</summary>
    public required string Group { get; set; }

    /// <summary>The accessible name for the group.</summary>
    public required string Label { get; set; }

    public int? Value { get; set; }

    public int? Max { get; set; }

    public UiSize? Size { get; set; }

    public Action<int>? OnChange { get; set; }

    public string? Class { get; set; }

    /// <inheritdoc />
    protected override Component? Render() =>
        Div
            .Role("radiogroup")
            .Aria(new Dictionary<string, string?> { ["label"] = Label })
            .Class(UiClass.Compose(
                "rating",
                Size is { } size ? UiClassNames.RatingSize(size) : "",
                Class))[
            Input
                .Value(Value is null or 0)
                .Type(InputType.Radio)
                .Name(Group)
                .Class("rating-hidden")
                .Aria(new Dictionary<string, string?> { ["label"] = "No rating" }),
            Enumerable.Range(1, Math.Max(Max ?? 5, 0)).Select(star =>
            {
                var input = Input
                    .Value(star == Value)
                    .Key(star)
                    .Type(InputType.Radio)
                    .Name(Group)
                    .Class("mask mask-star-2")
                    .Aria(new Dictionary<string, string?>
                    {
                        ["label"] = $"{star.ToString(System.Globalization.CultureInfo.InvariantCulture)} of "
                                    + (Max ?? 5).ToString(System.Globalization.CultureInfo.InvariantCulture),
                    });

                return OnChange is { } change ? input.OnChange(_ => change(star)) : input;
            })
        ];
}
