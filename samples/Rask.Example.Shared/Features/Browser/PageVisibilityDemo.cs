using Rask.Core.Browser;

namespace Rask.Example.Shared.Features;

/// <summary>
///     <see cref="IPageVisibility" /> — read whether the page is foreground/visible, e.g. to pause work
///     when the user tabs away.
/// </summary>
public sealed partial class PageVisibilityDemo(IPageVisibility visibility) : Component
{
    private string? _state;
    private string? _status;

    protected override Component? Render() =>
        Div.Class($"{Tw.Card} shadow-sm border-0")[
            Div.Class(Tw.CardBody)[
                Button.Class($"{Tw.BtnOutlinePrimary} mb-2").Type("button")
                    .Id("vis-read")
                    .OnClickAsync(Read)[
                    "Read visibility"],
                Div.Class("text-sm text-ui-muted")["State: ", Code.Id("vis-value")[_state ?? "(not read)"]],
                Div.Class("text-sm text-ui-muted")["Status: ", Code.Id("vis-status")[_status ?? "(idle)"]]
            ]
        ];

    private async Task Read()
    {
        try
        {
            var state = await visibility.GetStateAsync();
            var hidden = await visibility.IsHiddenAsync();
            _state = $"{state} (hidden: {hidden})";
            _status = "Read";
        }
        catch (Exception ex) { _status = "Read failed: " + ex.Message; }
    }
}
