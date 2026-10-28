namespace Rask.Site.Features.UiDocs;

/// <summary>How a phone field behaves: its value, the engine in the browser, and what it fetches.</summary>
internal sealed partial class UiPhoneBehaviour : Component
{
    /// <inheritdoc />
    protected override Component? Render() =>
        UiDocsBehaviour[
            Value(),
            Browser(),
            Engine()
        ];

    private static UiDocsTopic Value() =>
        UiDocsTopic.Key("value").Heading("The value is what Flux's is").Paragraphs([
            "The bound `string` is the number in E.164 (`+12015550123`) once it is a possible one, and the text as typed "
            + "until then (`201`), so it is not always E.164, and a possible number is not a checked one: validate it on "
            + "the server. An extension typed after the number is dropped from the value. Picking a country keeps the "
            + "number and reads it again for that country.",
        ]);

    private static UiDocsTopic Browser() =>
        UiDocsTopic.Key("browser").Heading("Everything is in the browser").Paragraphs([
            "The field runs the intl-tel-input release fluxui.dev runs, so a formatting quirk of Flux's is this "
            + "field's too, with Google's libphonenumber compiled. Typing, formatting, opening, searching and walking the "
            + "list send the server nothing; the number goes with the next action, as Flux's `wire:model` does, or as it "
            + "changes with `.Live()` or `.Debounce(…)`. The list names the countries in the page's language "
            + "(`Intl.DisplayNames`).",
        ]);

    private static UiDocsTopic Engine() =>
        UiDocsTopic.Key("engine").Heading("Two files, fetched when needed").Paragraphs([
            "The runtime fetches `wwwroot/js/rask-ui-phone.js` (21 KB gzipped) the first time a page carries a phone "
            + "field, and that fetches `rask-ui-phone-utils.js` (61 KB gzipped) when the field mounts. Every app's build "
            + "writes both, with their notices, and the flags the list shows. An app that will never draw one keeps its "
            + "`wwwroot` clean with `<RaskUiPhoneEngine>false</RaskUiPhoneEngine>`.",
            "Without script the field is a plain tel input beside the starting country, and a form posts the bound value.",
        ]);
}
