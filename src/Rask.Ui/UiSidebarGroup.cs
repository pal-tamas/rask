namespace Rask;

/// <summary>Flux's <c>flux:sidebar.group</c>: <see cref="UiSidebarItem" />s under a heading, which can fold away.</summary>
/// <remarks>
/// Expandable, it is a native <c>&lt;details&gt;</c>: it opens and closes with no script, and says which it is.
/// Narrowed to the rail a group shows its <see cref="Icon" /> alone — one without an icon is not shown.
/// </remarks>
public sealed partial class UiSidebarGroup : Component
{
    private const string Row =
        "my-px flex h-10 w-full min-w-0 cursor-default text-center list-none items-center rounded-lg border border-transparent text-zinc-500 "
        + "hover:bg-zinc-800/5 hover:text-zinc-800 sidebar-desktop:h-8 dark:text-white/80 dark:hover:bg-white/[7%] "
        + "dark:hover:text-white [&::-webkit-details-marker]:hidden";

    private const string Label = "block truncate text-sm font-medium";

    /// <summary>The words above the group's items.</summary>
    public string? Heading { get; set; }

    /// <summary>Lets the reader fold the group away and open it again.</summary>
    public bool? Expandable { get; set; }

    /// <summary>The icon before the heading, in place of the chevron — and what the group is in the rail.</summary>
    public Ui.IconName? Icon { get; set; }

    /// <summary>Whether an expandable group is open. Open, unless this says otherwise.</summary>
    public bool? Expanded { get; set; }

    /// <summary>Runs when the reader opens or folds the group, with the state asked for.</summary>
    public Callback<bool> OnToggle { get; set; }

    /// <summary>Classes for the group.</summary>
    public string? Class { get; set; }

    /// <inheritdoc />
    protected override Component? Render()
    {
        if (Expandable != true)
        {
            return Div.Class(UiClass.Compose("block sidebar-rail:hidden", Class)).Attributes(("data-ui-sidebar-group", ""))[
                Heading is { } heading
                    ? Div.Class("px-3 py-2 text-xs font-medium text-zinc-400")[heading]
                    : null,
                Div.Class("flex flex-col")[Children ?? []]
            ];
        }

        var details = Details.Open(Expanded != false)
            .Class(UiClass.Compose("group/disclosure grid sidebar-rail:hidden", Class))
            .Attributes(("data-ui-sidebar-group", ""));
        if (OnToggle.HasValue)
        {
            details = details.OnToggle(e => OnToggle.Invoke(string.Equals(e.NewState, "open", StringComparison.Ordinal)).AsTask());
        }

        return
        [
            details[
                Summary.Class(Row)[
                    Icon is { } icon
                        ? Div.Class("px-3")[Ui.Icon.Name(icon).Class("size-4")]
                        : Div.Class("px-3.5")[Chevrons()],
                    Span.Class(Icon is null ? Label : "block flex-1 truncate text-left text-sm font-medium rtl:text-right")[Heading ?? ""],
                    Icon is null ? null : Div.Class("ps-3 pe-2.5")[Chevrons()]
                ],
                Div.Class("relative hidden min-w-0 ps-7 group-open/disclosure:block")[
                    Div.Class("absolute inset-y-[3px] start-0 ms-5 w-px bg-zinc-200 dark:bg-white/30"),
                    Div.Class("flex flex-col")[Children ?? []]
                ]
            ],
            Icon is { } mark ? RailButton(mark) : null
        ];
    }

    private static Component Chevrons() =>
    [
        Ui.Icon.Name(Ui.IconName.ChevronDown).Class("hidden size-3 group-open/disclosure:block"),
        Ui.Icon.Name(Ui.IconName.ChevronRight).Class("size-3 group-open/disclosure:hidden")
    ];

    // Seam: in the rail Flux opens the group's items as a menu beside its icon. Ui.Dropdown and Ui.Menu go here
    // when they land; until then the icon widens the sidebar, which is where the items are.
    private Component RailButton(Ui.IconName icon) =>
        Div.Class("hidden sidebar-rail:flex").Attributes(("data-ui-sidebar-group-dropdown", ""), ("data-ui-seam", "dropdown"))[
            RaskMarkup.Label
                .For(UiSidebarState.Rail)
                .Class(
                    "my-px flex h-8 w-10 cursor-default items-center justify-center gap-3 rounded-lg border border-transparent "
                    + "px-3 text-center text-zinc-500 hover:bg-zinc-800/5 hover:text-zinc-800 dark:text-white/80 "
                    + "dark:hover:bg-white/[7%] dark:hover:text-white")
                .Role("button")
                .TabIndex(0)
                .Title(Heading ?? "")
                .Aria("label", Heading ?? "")[
                Div.Class("relative")[Ui.Icon.Name(icon).Class("size-4")]
            ]
        ];
}
