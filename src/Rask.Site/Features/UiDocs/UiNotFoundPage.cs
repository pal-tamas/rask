using Rask.Core.Routing;

namespace Rask.Site.Features.UiDocs;

/// <summary>
///     The kit's not-found and forbidden callouts — not Flux components, an addition the owner approved — their examples
///     with their code, and their reference.
/// </summary>
[Route("not-found")]
[ParentRoute(typeof(UiDocsLayout))]
public sealed partial class UiNotFoundPage : Component
{
    /// <inheritdoc />
    protected override Component? HeadAssets => UiDocsCatalog.NotFound.Meta();

    /// <inheritdoc />
    protected override Component? Render() =>
        UiComponentDoc
            .Entry(UiDocsCatalog.NotFound)
            .Lead("What an address no page answers and a page the reader may not open say, inside the app's frame: the menu "
                  + "stays, one sentence stands where the page would have. Not one of Flux UI's components: the kit's own, "
                  + "drawn as Flux's warning and danger callouts and worded from the RaskStrings catalog — a Hungarian "
                  + "reader reads „A kért oldal nem található.” and „Nincs jogosultsága az oldal megtekintéséhez.”")
            .Api(UiApi.NotFound)[
            UiDocsExample.Key("ui-not-found").Heading("Not found").File("NotFoundExample.cs").Demo(NotFoundExample).Testid("ui-not-found")
                .Blurb("An app on the kit draws this by itself at an address no page answers, inside its root layout — the "
                       + "route at / its pages nest under — and answers 404. It needs no not-found page of its own; one it "
                       + "declares with [NotFound] still wins, and can draw Ui.NotFound. Home(route) is where the button leads."),
            UiDocsExample.Key("ui-forbidden").Heading("Forbidden").File("ForbiddenExample.cs").Demo(ForbiddenExample).Testid("ui-forbidden")
                .Blurb("A page whose [Authorize] turns a signed-in reader away shows this in its layout's outlet by itself. A "
                       + "page that decides for itself draws it in place of its content: !allowed ? Ui.Forbidden : …. A "
                       + "refused save says the same sentence in its toast: RaskStrings.Get(RaskString.PageForbidden, …).")
        ];
}
