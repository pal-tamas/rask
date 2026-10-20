namespace Rask.Site.Features.UiDocs;

/// <summary>
///     A component page's reference: each part, the Flux part it mirrors, and a table of its own steps.
/// </summary>
/// <remarks>
///     An enum's values come from the enum, so a value added to the kit appears here with no edit. The steps
///     themselves are written out, and <c>UiApiReferenceTests</c> holds them to the component's properties both ways.
/// </remarks>
internal sealed partial class UiApiReference : Component
{
    // The generator stops writing a step per value above this many (ComponentFactoryGenerator's MaxEnumStepMembers).
    private const int MostValueSteps = 24;

    /// <summary>The parts to list, in the page's order.</summary>
    public required IReadOnlyList<UiApiPart> Parts { get; set; }

    /// <inheritdoc />
    protected override Component? Render() =>
        Section.Id("reference").Data("testid", "ui-api").Class("mt-12 space-y-10")[
            Ui.Heading.Level(2).Xl["Reference"],
            Parts.Select(Part)
        ];

    private static Component Part(UiApiPart part) =>
        Div.Key(part.Entry).Data("testid", "ui-api-part").Class("space-y-3")[
            Div.Class("flex flex-wrap items-center gap-2")[
                Ui.Heading.Level(3).Lg[Code[part.Entry]],
                Ui.Badge.Sm[part.Flux]
            ],
            Ui.Text[part.Summary],
            part.Steps.Count == 0
                ? Ui.Text.Subtle["No steps of its own: what goes inside it is its content."]
                : Steps(part.Steps),
            typeof(Element).IsAssignableFrom(part.Component)
                ? Ui.Text.Sm.Subtle["It is one HTML element, so Id, Class, Style, Data, Aria and the events are its steps too."]
                : null
        ];

    private static Component Steps(IReadOnlyList<UiApiStep> steps) =>
        Ui.Table[
            Ui.TableColumns[
                Ui.TableColumn["Step"],
                Ui.TableColumn["Takes"],
                Ui.TableColumn["What it does"]
            ],
            Ui.TableRows[
                steps.Select(step => Ui.TableRow.Key(step.Name)[
                    Ui.TableCell.Variant(Ui.TableCellVariant.Strong).Class("align-top")[Code[StepCall(step)]],
                    Ui.TableCell.Class("min-w-40 align-top whitespace-normal")[Takes(step)],
                    Ui.TableCell.Class("min-w-64 align-top whitespace-normal")[step.Description]
                ])
            ]
        ];

    // A flag reads as a bare call — `.Square()` sets it, `.Square(false)` clears it — and everything else takes a value.
    private static string StepCall(UiApiStep step) =>
        Bare(step.Takes) == typeof(bool) ? $".{step.Name}()" : $".{step.Name}(…)";

    private static Component Takes(UiApiStep step)
    {
        var type = Bare(step.Takes);
        if (step.TakesText is { } text)
        {
            return Span[text];
        }

        if (!type.IsEnum)
        {
            return Code[TypeName(type)];
        }

        var names = Enum.GetNames(type);
        if (names.Length > MostValueSteps)
        {
            return Span[Code[TypeName(type)], $" — one of {names.Length} names"];
        }

        var flags = type.IsDefined(typeof(FlagsAttribute), inherit: false);
        return Div.Class("space-y-1")[
            Code[TypeName(type)],
            Div.Class("flex flex-wrap gap-1")[names.Select(name => Ui.Badge.Key(name).Sm[name])],
            Ui.Text.Sm.Subtle[flags ? "Combined with |." : "Each is a step of its own too, unless an HTML tag has its name."]
        ];
    }

    private static Type Bare(Type type) => Nullable.GetUnderlyingType(type) ?? type;

    private static string TypeName(Type type) => type switch
    {
        _ when type == typeof(string) => "string",
        _ when type == typeof(bool) => "bool",
        _ when type == typeof(int) => "int",
        { DeclaringType: { } outer } => outer.Name + "." + type.Name,
        _ => type.Name,
    };
}
