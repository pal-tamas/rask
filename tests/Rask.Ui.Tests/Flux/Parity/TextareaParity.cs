using Rask.Core;

namespace Rask.UiTests.Flux.Parity;

/// <summary><c>fluxui.dev/components/textarea</c>, example by example.</summary>
public sealed partial class TextareaParity : FluxParity
{
    // The column Flux's docs page sets every textarea example in.
    private const string Column = "max-width:384px;margin:0 auto";

    // What an app's own Tailwind build emits for the class the resize examples are spaced with.
    private const string AppUtilities = "<style>.mb-4{margin-bottom:1rem}</style>";

    public override string Page => "textarea";

    public override IEnumerable<(string Section, Component Example)> Examples()
    {
        // The rendered page labels its first example; the markdown shows it bare.
        yield return ("", Div.Style(Column)[
            Raw.Value(AppUtilities),
            Ui.Textarea.Of<string>().Label("Description")
        ]);

        yield return ("with-placeholder", Div.Style(Column)[
            Ui.Textarea.Of<string>().Label("Order notes").Placeholder("No lettuce, tomato, or onion...")
        ]);

        yield return ("fixed-row-height", Div.Style(Column)[Ui.Textarea.Of<string>().Rows(2).Label("Note")]);

        yield return ("auto-sizing-textarea", Div.Style(Column)[
            Ui.Textarea.Of<string>().Rows(UiTextareaRows.Auto).Placeholder("This textarea will adjust to fit the content...")
        ]);

        yield return ("configure-resize", Div.Style(Column)[
            Ui.Textarea.Of<string>().Rows(2).Vertical.Placeholder("Resize \"vertical\"").Class("mb-4"),
            Ui.Textarea.Of<string>().Rows(2).None.Placeholder("Resize \"none\"").Class("mb-4"),
            Ui.Textarea.Of<string>().Rows(2).Horizontal.Placeholder("Resize \"horizontal\"").Class("mb-4"),
            Ui.Textarea.Of<string>().Rows(2).Both.Placeholder("Resize \"both\"")
        ]);
    }
}
