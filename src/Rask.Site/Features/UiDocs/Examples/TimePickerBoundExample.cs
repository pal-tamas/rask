using System.Globalization;

namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class TimePickerBoundExample : Component
{
    private readonly Meeting _meeting = new();

    protected override Component? Render() =>
        Div.Class("max-w-xs space-y-3")[
            Ui.TimePicker.Bind(() => _meeting.StartsAt).Live().Label("Starts at"),
            Ui.Text[_meeting.StartsAt is { } at ? "Starts at " + at.ToString("HH:mm", CultureInfo.InvariantCulture) + "." : "No time yet."]
        ];

    public sealed class Meeting
    {
        public TimeOnly? StartsAt { get; set; } = new TimeOnly(11, 30);
    }
}
