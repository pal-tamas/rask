
namespace Rask.Site.Features;

/// <summary>
///     MDN's <c>document.visibilityState</c>, from Rask.Web — read whether the page is foreground/visible, e.g. to pause
///     work when the user tabs away.
/// </summary>
public sealed partial class PageVisibilityDemo : Component
{
    private string? _state;
    private string? _status;

    protected override Component? Render() =>
        Ui.Card[
                Ui.Button.Primary.Outline.Class("mb-2")
                    .Id("vis-read")
                    .OnClick(Read)["Read visibility"],
                Div.Class("text-sm text-ui-muted")["State: ", Code.Id("vis-value")[_state ?? "(not read)"]],
                Div.Class("text-sm text-ui-muted")["Status: ", Code.Id("vis-status")[_status ?? "(idle)"]]
            ];

    private async Task Read()
    {
        try
        {
            _state = $"{await Document.VisibilityState} (hidden: {await Document.Hidden})";
            _status = "Read";
        }
        catch (Exception ex) { _status = "Read failed: " + ex.Message; }
    }
}
