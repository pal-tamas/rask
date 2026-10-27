using Rask.Core;

namespace Rask.UiTests.Flux.Parity;

/// <summary><c>fluxui.dev/components/composer</c>, example by example, as the live page renders them.</summary>
/// <remarks>
///     The live examples are fuller than the markdown's: each carries the same five buttons, the first a
///     description for screen readers, the input variant a name field above it and a send button under it.
///     The invalid example's message is the one Flux's server writes for an empty prompt.
/// </remarks>
public sealed partial class ComposerParity : FluxParity
{
    // The columns Flux's docs page sets the examples in.
    private const string Narrow = "max-width:384px;margin:0 auto";
    private const string Medium = "max-width:448px;margin:0 auto";
    private const string Small = "max-width:320px;margin:0 auto";

    // What the header example's own markup is drawn with: an app's utilities, under names of the page's.
    private const string AppClasses =
        "<style>.parity-thumb{position:relative;border:1px solid oklch(0.92 0.004 286.32);border-radius:8px;overflow:hidden}"
        + ".dark .parity-thumb{border-color:oklch(0.37 0.013 285.805)}"
        + ".parity-thumb img{display:block;width:56px;height:56px}"
        + ".parity-corner{position:absolute;top:0;right:0;padding:4px}"
        + ".parity-close{display:flex;justify-content:center;align-items:center;padding:2px;border-radius:calc(infinity * 1px);"
        + "background:color-mix(in oklab,oklch(0.21 0.006 285.885) 50%,transparent)}"
        // `hover:` as Tailwind writes it: only where there is a pointer to hover (a phone has none).
        + "@media (hover:hover){.parity-close:hover{background:color-mix(in oklab,oklch(0.21 0.006 285.885) 70%,transparent)}}"
        + ".parity-white{color:#fff}"
        + ".parity-fields>*{margin-bottom:24px}"
        + ".parity-send{display:flex;justify-content:flex-end}.parity-send>*{width:100%}</style>";

    private const string Prompt = "How can I help you today?";

    public override string Page => "composer";

    public override IEnumerable<(string Section, Component Example)> Examples()
    {
        yield return ("", Div.Style(Narrow)[
            Raw.Value(AppClasses),
            Actions(Ui.Composer.Of<string>().Label("Prompt").LabelSrOnly().Description("Enter your prompt here").DescriptionSrOnly().Placeholder(Prompt))
        ]);

        yield return ("with-header", Div.Style(Medium)[
            Actions(Ui.Composer.Of<string>().Placeholder(Prompt).Header(
                Div.Class("parity-thumb")[
                    Img.Src("https://fluxui.dev/img/demo/caleb.png").Alt("Profile picture"),
                    Div.Class("parity-corner")[
                        Button.Type(ButtonType.Button).Class("parity-close")[
                            Ui.Icon.Name(Ui.IconName.XMark).Variant(Ui.IconVariant.Micro).Class("parity-white")
                        ]
                    ]
                ]))
        ]);

        yield return ("inline", Div.Style(Medium)[
            Ui.Composer.Of<string>().Rows(1).Inline().Placeholder(Prompt)
                .ActionsLeading(Ui.Button.Sm.Ghost.Icon(Ui.IconName.Plus))
                .ActionsTrailing([
                    Ui.Button.Sm.Filled.Icon(Ui.IconName.Microphone),
                    Ui.Button.Type(Ui.ButtonType.Submit).Sm.Primary.Icon(Ui.IconName.PaperAirplane),
                ])
        ]);

        yield return ("input-variant", Div.Style(Small)[
            Div.Class("parity-fields")[
                Ui.Input.Of<string>().Label("Name"),
                Ui.Composer.Of<string>().Variant(Ui.ComposerVariant.Input).Label("Message").Placeholder("What's on your mind?")
                    .ActionsLeading(Leading())
                    .ActionsTrailing(Ui.Button.Sm.Filled.Icon(Ui.IconName.Microphone)),
                Div.Class("parity-send")[Ui.Button.Primary.Icon(Ui.IconName.PaperAirplane)["Send"]]
            ]
        ]);

        yield return ("height", Div.Style(Medium)[Actions(Ui.Composer.Of<string>().Rows(4).MaxRows(8).Placeholder(Prompt))]);

        yield return ("submit-behavior", Div.Style(Medium)[
            Form.Model(new object())[Actions(Ui.Composer.Of<string>().Enter.Placeholder(Prompt))]
        ]);

        yield return ("rich-text", Div.Style(Medium)[
            Actions(Ui.Composer.Of<string>().Rows(3).Placeholder(Prompt)
                .Input(Ui.Editor.Toolbar("bold italic bullet ordered | link | align")))
        ]);

        yield return ("disabled", Div.Style(Medium)[Actions(Ui.Composer.Of<string>().Placeholder(Prompt).Disabled())]);

        // Flux's server decides the message; here it is stated, in a field written around the composer.
        yield return ("invalid", Div.Style(Medium)[
            Ui.Field[
                Ui.Label.Class("sr-only")["Message"],
                Actions(Ui.Composer.Of<string>().Placeholder(Prompt).Invalid()),
                Ui.Error.Message("The prompt field is required.")
            ]
        ]);
    }

    // The five buttons every live example but the inline one carries.
    private static UiComposer<string> Actions(UiComposer<string> composer) =>
        composer
            .ActionsLeading(Leading())
            .ActionsTrailing([
                Ui.Button.Sm.Filled.Icon(Ui.IconName.Microphone),
                Ui.Button.Type(Ui.ButtonType.Submit).Sm.Primary.Icon(Ui.IconName.PaperAirplane),
            ]);

    private static Component Leading() =>
    [
        Ui.Button.Sm.Subtle.Icon(Ui.IconName.PaperClip),
        Ui.Button.Sm.Subtle.Icon(Ui.IconName.Slash),
        Ui.Button.Sm.Subtle.Icon(Ui.IconName.AdjustmentsHorizontal),
    ];
}
