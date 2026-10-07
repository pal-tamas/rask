using Rask.Core;

namespace Rask.UiTests.Flux.Parity;

/// <summary><c>fluxui.dev/components/input</c>, example by example.</summary>
/// <remarks>
///     <para>
///     The words are the rendered page's, which says more than its markdown does: the read-only example has a
///     label and a value there, the clearable one a value.
///     </para>
///     <para>
///     Flux's button and its select are other pages' components and not on this branch, so each is a stand-in
///     marked <c>data-parity-skip</c>: the room it takes on Flux's page, held to its place and size.
///     </para>
/// </remarks>
public sealed partial class InputParity : FluxParity
{
    // The column Flux's docs page sets every input example in.
    private const string Column = "max-width:384px;margin:0 auto";

    private const string Grid = Column + ";display:grid;gap:24px";

    // What an app's own Tailwind build emits for the classes these examples hand to Class: the kit's sheet
    // holds only what the kit writes.
    private const string AppUtilities = "<style>.mb-6{margin-bottom:1.5rem}.min-w-\\[180px\\]{min-width:180px}</style>";

    public override string Page => "input";

    public override IEnumerable<(string Section, Component Example)> Examples()
    {
        yield return ("", Div.Style(Column)[
            Raw.Value(AppUtilities),
            Ui.Field[
                Ui.Label["Username"],
                Ui.Description["This will be publicly displayed."],
                Ui.Input.Of<string>(),
                Ui.Error.Name("username")
            ]
        ]);

        // "Shorthand" and "Class targeting" have no rendered example on Flux's page.
        yield return ("types", Div.Style(Grid)[
            Ui.Input.Value("caleb@gmail.com").Type(InputType.Email).Label("Email"),
            Ui.Input.Value("password").Type(InputType.Password).Label("Password"),
            Ui.Input.Of<string>().Type(InputType.Date).Max("2999-12-31").Label("Date")
        ]);

        // wire:model is Livewire's; here a file input reports through OnFiles.
        yield return ("file", Div.Style(Grid)[
            Ui.Input.Of<string>().Type(InputType.File).Label("Logo"),
            Ui.Input.Of<string>().Type(InputType.File).Label("Attachments").Multiple()
        ]);

        yield return ("smaller", Div.Style(Column)[Ui.Input.Of<string>().Sm.Placeholder("Filter by...")]);

        yield return ("disabled", Div.Style(Column)[Ui.Input.Of<string>().Disabled().Label("Email")]);

        yield return ("readonly", Div.Style(Column)[
            Ui.Input.Value("BA7K7QZ511S8Z2K").ReadOnly().Filled.Label("Public API key")
        ]);

        yield return ("invalid", Div.Style(Column)[Ui.Input.Of<string>().Invalid()]);

        // The second is mask:dynamic="$money($input)", an Alpine expression: it has no Rask.Ui twin, and an
        // input holding the same value takes the same room.
        yield return ("input-masking", Div.Style(Grid)[
            Ui.Input.Value("7161234567").Mask("(999) 999-9999"),
            Ui.Input.Value("1234.56")
        ]);

        yield return ("icons", Div.Style(Grid)[
            Ui.Input.Of<string>().Icon(Ui.IconName.MagnifyingGlass).Placeholder("Search orders"),
            Ui.Input.Of<string>().IconTrailing(Ui.IconName.CreditCard).Placeholder("4444-4444-4444-4444"),
            Ui.Input.Of<string>().IconTrailing(Ui.IconName.Loading).Placeholder("Search transactions")
        ]);

        yield return ("icon-buttons", Div.Style(Column)[
            Ui.Input.Of<string>().Placeholder("Search orders").IconTrailing(SubtleButton(Ui.IconName.XMark)).Class("mb-6"),
            Ui.Input.Value("password").Type(InputType.Password).IconTrailing(SubtleButton(Ui.IconName.Eye))
        ]);

        yield return ("clearable,-copyable,-and-viewable-inputs", Div.Style(Column)[
            Ui.Input.Value("Jack Skellington").Placeholder("Search orders").Clearable().Class("mb-6"),
            Ui.Input.Value("password").Type(InputType.Password).Viewable().Class("mb-6"),
            Ui.Input.Value("FLUX-1234-5678-ABCD-EFGH").Icon(Ui.IconName.Key).ReadOnly().Copyable()
        ]);

        yield return ("keyboard-hint", Div.Style(Column)[
            Ui.Input.Of<string>().Kbd("⌘K").Icon(Ui.IconName.MagnifyingGlass).Placeholder("Search...")
        ]);

        // Flux's page gives this one a width of its own; the button carries no data-flux-* marker, so the tool
        // pairs only its icon and the rest was compared by hand (see the report in the commit).
        yield return ("as-a-button", Div.Style(Column + ";display:flex;justify-content:center")[
            Div[Ui.Input.Of<string>().As(Ui.InputAs.Button).Placeholder("Search...").Icon(Ui.IconName.MagnifyingGlass).Kbd("⌘K").Class("min-w-[180px]")]
        ]);

        yield return ("with-buttons", Div.Style(Column)[
            Ui.InputGroup.Class("mb-6")[
                Ui.Input.Of<string>().Placeholder("Post title"),
                Ui.Button.Icon(Ui.IconName.Plus)["New post"]
            ],
            Ui.InputGroup[
                StandIn("width:85px"),
                Ui.Input.Of<string>().Placeholder("$99.99")
            ]
        ]);

        yield return ("text-prefixes-and-suffixes", Div.Style(Column)[
            Ui.InputGroup.Class("mb-6")[
                Ui.InputGroupPrefix["https://"],
                Ui.Input.Of<string>().Placeholder("example.com")
            ],
            Ui.InputGroup[
                Ui.Input.Of<string>().Placeholder("chunky-spaceship"),
                Ui.InputGroupSuffix[".brand.com"]
            ]
        ]);

        yield return ("input-group-labels", Div.Style(Column)[
            Ui.Field[
                Ui.Label["Website"],
                Ui.InputGroup[
                    Ui.InputGroupPrefix["https://"],
                    Ui.Input.Of<string>().Id("website").Placeholder("example.com")
                ],
                Ui.Error.Name("website")
            ]
        ]);
    }

    private static readonly Dictionary<string, string?> StandInMarks = new(StringComparer.Ordinal)
    {
        ["data-parity-skip"] = string.Empty,
        ["data-ui-group-target"] = string.Empty,
    };

    // Flux's button or native select beside an input in a group: the room it takes, nothing more.
    private static Component StandIn(string width) =>
        Div.Attributes(StandInMarks).Style("height:40px;flex:none;" + width);

    // <flux:button size="sm" variant="subtle" icon="x-mark" class="-mr-1" /> in the input's trailing slot.
    private static Component SubtleButton(Ui.IconName icon) =>
        Ui.Button.Subtle.Sm.Icon(icon).Style("margin-right:-4px");
}
