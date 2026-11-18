using System.ComponentModel.DataAnnotations;
using System.Globalization;

namespace Rask.Site.Features.UiDocs.Examples;

// A day and a time the model keeps apart: each part binds its own member and says its own message under itself.
public sealed partial class DayAndTimePartsExample : Component
{
    private readonly Slot _slot = new();
    private string _said = "Nothing saved yet.";

    protected override Component? Render() =>
        Div.Class("space-y-3")[
            Form.Model(_slot).OnSubmit(Save).Id("udt-parts-form").Class("max-w-md space-y-6")[
                Ui.DayAndTime.Bind(() => _slot.Day, () => _slot.Time).Label("Delivered").Id("udt-parts"),
                Ui.Button.Type(Ui.ButtonType.Submit).Primary["Save"]
            ],
            Ui.Text.Id("udt-parts-said")[_said]
        ];

    private void Save(Slot slot) =>
        _said = "Saved " + slot.Day?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
                + " at " + slot.Time?.ToString("HH:mm", CultureInfo.InvariantCulture) + ".";

    public sealed class Slot
    {
        [Required]
        [NoSunday]
        public DateOnly? Day { get; set; }

        [Required]
        public TimeOnly? Time { get; set; }
    }

    // A rule of the day alone: nothing is delivered on a Sunday.
    [AttributeUsage(AttributeTargets.Property)]
    private sealed class NoSundayAttribute : ValidationAttribute
    {
        public override bool IsValid(object? value) => value is not DateOnly { DayOfWeek: DayOfWeek.Sunday };

        public override string FormatErrorMessage(string name) => "Nothing is delivered on a Sunday.";
    }
}
