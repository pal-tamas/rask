using System.ComponentModel.DataAnnotations;
using System.Globalization;

namespace Rask.Site.Features.UiDocs.Examples;

// A required moment with seconds, in Hungarian: a new day keeps 14:35:42, and a time left at 14:35 keeps the 42 seconds.
public sealed partial class DayAndTimeSecondsExample : Component
{
    private readonly Reading _reading = new();
    private string _said = "Nothing saved yet.";

    protected override Component? Render() =>
        Div.Class("space-y-3")[
            Form.Model(_reading).OnSubmit(Save).Id("udt-seconds-form").Class("max-w-md space-y-6")[
                Ui.DayAndTime.Bind(() => _reading.Taken).Locale("hu-HU").Label("Leolvasva").Id("udt-taken"),
                Ui.Button.Type(Ui.ButtonType.Submit).Primary["Mentés"]
            ],
            Ui.Text.Id("udt-seconds-said")[_said]
        ];

    private void Save(Reading reading) =>
        _said = "Saved " + reading.Taken?.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) + ".";

    public sealed class Reading
    {
        [Required]
        public DateTime? Taken { get; set; } = new DateTime(2026, 11, 6, 14, 35, 42, DateTimeKind.Unspecified);
    }
}
