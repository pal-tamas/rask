using Rask.Core.Routing;
using Rask.Site.Features.UiDocs;

namespace Rask.Site.Features.UiKit;

/// <summary>
///     Where the kit's Data input page was: its components each have a page in the UI tab now, so this sends the
///     reader on to the first of them.
/// </summary>
/// <remarks>
///     The static host cannot answer with a 301, so the old URL keeps a page of its own: it names the date picker's
///     page canonical, which also keeps it out of the sitemap, goes there as it mounts, and links to every page that
///     took a component, for a reader whose browser runs no script.
/// </remarks>
[Route("ui/data-input")]
[ParentRoute(typeof(ShowcaseLayout))]
public sealed partial class UiKitDataInputPage : Component
{
    /// <inheritdoc />
    protected override Component? HeadAssets => UiDocsCatalog.DatePicker.Meta();

    /// <inheritdoc />
    protected override async Task OnFirstRender() => Go.To(PageMeta.LinkTo(UiDocsCatalog.DatePicker.Path)).Replacing();

    /// <inheritdoc />
    protected override Component? Render() =>
    [
        Ui.Heading.Level(1).Xl["Data input"],
        Ui.Text.Class("mt-2")["The form controls, pickers and rich inputs each have a page of their own in the UI tab now."],
        UiKitMovedPages.Pages([
            UiDocsCatalog.Field, UiDocsCatalog.Input, UiDocsCatalog.Textarea, UiDocsCatalog.Select, UiDocsCatalog.Checkbox,
            UiDocsCatalog.Radio, UiDocsCatalog.Switch, UiDocsCatalog.Slider, UiDocsCatalog.Otp, UiDocsCatalog.Composer,
            UiDocsCatalog.Autocomplete, UiDocsCatalog.Pillbox, UiDocsCatalog.Toggle, UiDocsCatalog.Phone,
            UiDocsCatalog.Calendar, UiDocsCatalog.DatePicker, UiDocsCatalog.TimePicker, UiDocsCatalog.ColorPicker,
            UiDocsCatalog.FileUpload, UiDocsCatalog.Editor,
        ])
    ];
}
