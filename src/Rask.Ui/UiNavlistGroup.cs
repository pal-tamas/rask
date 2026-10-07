namespace Rask;

/// <summary>
///     Flux's <c>flux:navlist.group</c>: a titled run of <see cref="UiNavlistItem" />s, optionally one that folds
///     away under its heading.
/// </summary>
/// <remarks>
///     <para>
///     Plain, it is a heading over its items. <see cref="Expandable" /> makes it a disclosure: the heading
///     becomes the control, a chevron says which way it is, and a rule joins the items under it. It is a
///     <c>&lt;details&gt;</c>, so it opens and closes — click, Enter, Space — with no runtime at all, and is
///     open unless <see cref="Expanded" /> says otherwise, as Flux's is.
///     </para>
///     <para>
///     <see cref="Expanded" /> with <see cref="OnExpandedChange" /> hands the state to the page: a navigation
///     that opens the group holding the current page, or a filter that opens every group with a match.
///     </para>
/// </remarks>
public sealed partial class UiNavlistGroup : Component
{
    /// <summary>The group's title.</summary>
    public string? Heading { get; set; }

    /// <summary>Folds the items away under the heading.</summary>
    public bool? Expandable { get; set; }

    /// <summary>Whether an expandable group is open. Open unless this is <see langword="false" />.</summary>
    public bool? Expanded { get; set; }

    /// <summary>Runs when the reader opens or closes an expandable group, with the state it is now in.</summary>
    public Callback<bool> OnExpandedChange { get; set; }

    /// <summary>Classes for the call site, added to the group's own — the space above it, most often.</summary>
    public string? Class { get; set; }

    /// <inheritdoc />
    protected override Component? Render() => Expandable == true ? Disclosure() : Titled();

    private Component Titled() =>
        Div.Class("block", Class)[
            Heading is { } heading
                ? Div.Class("ui-rail-hide mb-[2px] px-3 py-2")[
                    Div.Class("text-sm leading-none font-medium text-zinc-400")[heading]
                ]
                : null,
            Div[Children ?? []]
        ];

    private Component Disclosure()
    {
        var details = Details.Open(Expanded != false).Class("group/disclosure block", Class);
        if (OnExpandedChange.HasValue)
        {
            details = details.OnToggle(e => OnExpandedChange.Invoke(string.Equals(e.NewState, "open", StringComparison.Ordinal)).AsTask());
        }

        return details.Attributes(("data-ui-navlist-group", null))[
            Summary.Class(
                "mb-[2px] flex h-8 w-full cursor-default list-none items-center rounded-lg text-center text-zinc-500 "
                + "hover:bg-zinc-800/5 hover:text-zinc-800 dark:text-white/80 dark:hover:bg-white/[7%] "
                + "dark:hover:text-white [&::-webkit-details-marker]:hidden")[
                Div.Class("ps-3 pe-4")[
                    Ui.Icon.Name(Ui.IconName.ChevronDown).Class("hidden size-3 group-open/disclosure:block"),
                    Ui.Icon.Name(Ui.IconName.ChevronRight).Class("block size-3 group-open/disclosure:hidden rtl:rotate-180")
                ],
                Span.Class("ui-rail-hide text-sm leading-none font-medium")[Heading ?? ""]
            ],
            Div.Class("relative ps-7")[
                Div.Class("absolute inset-y-[3px] start-0 ms-4 mb-[2px] w-px bg-zinc-200 dark:bg-white/30"),
                Children
            ]
        ];
    }
}
