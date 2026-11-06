using Microsoft.Playwright;

namespace Rask.Flux.Preparations;

/// <summary>
///     A picker measured open, for <c>flux parity &lt;slug&gt;-open --prepare picker-open</c> (parity-date runs it for
///     the date, time and datetime pickers). Flux's popup is placed by its script: turned over a trigger with no room
///     under it, held to the height the viewport leaves it. The kit's is placed by the runtime's hooks (rask-place.ts),
///     so the Rask page is given them, as an app has them — without, its popup hangs where the anchor rules alone put
///     it, as tall as its content, and is compared with a panel no app would show.
/// </summary>
[ParityPreparation("picker-open")]
public sealed class PickerOpen : IParityPreparation
{
    /// <summary>
    ///     Runs in the page. Marks what a reader presses to open each picker: the picker itself where its trigger fills it
    ///     (a press at the centre lands on the button), or the button beside the typed fields where the trigger is an
    ///     input — a press on a field only puts the caret in it.
    /// </summary>
    public const string Mark = """
        function mark(slug) {
          for (const picker of document.querySelectorAll(`[data-preview-wrapper] [data-flux-${slug}]`)) {
            // …a button where the trigger has one, else the chevron at the end of the typed field.
            const beside = picker.querySelector(`[data-flux-${slug}-trigger] button, ui-${slug}-trigger button`)
              ?? picker.querySelector(`ui-${slug}-trigger > svg:last-child`);
            (beside ?? picker).setAttribute('data-parity-open', '');
          }
        }
        """;

    private string flux = "";

    /// <summary>Flux's page: the one parity was asked for, less <c>-open</c>.</summary>
    public string Flux(string slug) => flux = slug.EndsWith("-open", StringComparison.Ordinal) ? slug[..^"-open".Length] : slug;

    /// <summary>
    ///     A browser that SHOWS its scrollbars, as a reader's does (parity-date says why): headless hides them, Flux's page
    ///     lock then reserves a gutter no scrollbar takes, and the page under its open dialog reflows by 15px.
    /// </summary>
    public BrowserTypeLaunchOptions Launch => new() { IgnoreDefaultArgs = ["--hide-scrollbars"] };

    /// <inheritdoc />
    public IReadOnlyList<string> Runtime => ["rask-hooks.ts"];

    /// <summary>Flux's page has its pickers marked as the Rask page writes them (data-parity-open).</summary>
    public async Task Prepare(IPage page, string scheme, string side)
    {
        if (side == "flux")
        {
            await page.Run(Mark, flux);
        }
    }
}
