namespace Rask;

/// <summary>Flux UI's <c>flux:command.items</c>: the list of a <see cref="UiCommand" />'s <see cref="UiCommandItem" />s.</summary>
/// <remarks>
///     A <c>listbox</c> the search box names its lit command in (<c>aria-activedescendant</c>), with Flux's "No results
///     found" at its foot, shown while the search leaves nothing. <c>aria-multiselectable</c> is Flux's.
/// </remarks>
public sealed partial class UiCommandItems : Component
{
    private static readonly UiPartMarker Marker = new("ui-command-items");

    private readonly int _instance = UiInstanceCounter.Next();

    /// <summary>Classes for the list.</summary>
    public string? Class { get; set; }

    /// <inheritdoc />
    protected override Component? Render()
    {
        Component?[] content =
        [
            .. Children ?? [],
            // Hidden until the search leaves no command shown: the runtime says so (rask-command.ts).
            Div.Class("block data-hidden:hidden").Attributes(("data-hidden", null), ("data-rask-command-empty", null))[
                Div.Class("flex h-10 items-center justify-center")[
                    Div.Class("text-sm font-medium text-zinc-500 dark:text-zinc-400")[
                        RaskStrings.Get(RaskString.CommandEmpty, "No results found")
                    ]
                ]
            ]
        ];

        return Div.Id("ui-command-items-" + _instance.ToString(System.Globalization.CultureInfo.InvariantCulture))
            .Class(UiClass.Compose("block cursor-default overflow-auto bg-white p-[5px] dark:bg-zinc-700", Class))
            .Data(Marker.With(null))
            .Role("listbox")
            .TabIndex(-1)
            .Aria("multiselectable", "true")[content];
    }
}
