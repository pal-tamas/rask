namespace Rask.Site.Features.UiDocs;

/// <summary>How a flag behaves: which codes it knows, where its drawings come from, and how they reach the page.</summary>
internal sealed partial class UiFlagBehaviour : Component
{
    /// <inheritdoc />
    protected override Component? Render() =>
        UiDocsBehaviour[
            Registry(),
            Files()
        ];

    private static UiDocsTopic Registry() =>
        UiDocsTopic.Key("registry").Heading("The codes and the drawings").Paragraphs([
            "The registry is Unicode's two-letter flag sequences, as Flux's is, and saying a code is in it takes no "
            + "position on any country, territory or border. The drawings are country-flag-icons 1.6.20 (MIT), the set "
            + "Flux serves; `UN` and `CQ` are not in that set and draw the empty frame, as does any code with no "
            + "drawing.",
        ]);

    private static UiDocsTopic Files() =>
        UiDocsTopic.Key("files").Heading("The drawings are files").Paragraphs([
            "The 254 drawings are not part of the assembly: 172 KB an app that shows three flags should not download. "
            + "An app's build copies them into `wwwroot/flags` with their notice (`wwwroot/flags/LICENSE.txt`), "
            + "together with the phone field's engine, whose list draws a flag a country. An app that turned that "
            + "engine off, or a host that is not a Web SDK project, says it wants them with "
            + "`<RaskUiWriteFlags>true</RaskUiWriteFlags>` in a `<PropertyGroup>` of its project file; `false` writes "
            + "none, phone engine or not.",
            "Each `Ui.Flag` is then one `<img loading=\"lazy\">` of `/flags/<CC>.svg`: a page fetches the flags it "
            + "shows. In a one-project WebAssembly app the browser half writes them, as it does the editor's engine. "
            + "Without them every flag is a broken image.",
        ]);
}
