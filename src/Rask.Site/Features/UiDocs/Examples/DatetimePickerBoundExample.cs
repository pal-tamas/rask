using System.Globalization;

namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class DatetimePickerBoundExample : Component
{
    private readonly Event _event = new();

    protected override Component? Render() =>
        Div.Class("max-w-xs space-y-3")[
            Ui.DatetimePicker.Bind(() => _event.StartsAt).Live().Label("Starts at"),
            Ui.Text[_event.StartsAt is { } at ? "Starts " + at.ToString("yyyy-MM-dd'T'HH:mm", CultureInfo.InvariantCulture) + "." : "Not scheduled."]
        ];

    public sealed class Event
    {
        public DateTime? StartsAt { get; set; }
    }
}
