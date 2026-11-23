using System.ComponentModel.DataAnnotations;

namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class FieldAutoExample : Component
{
    private readonly Booking _booking = new();

    protected override Component? Render() =>
        Div.Class("space-y-4")[
            Form.Model(_booking)[
                Div.Class("grid gap-4 sm:grid-cols-2")[
                    // Label, help text and placeholder come off [Display]; the badge off [Required].
                    Ui.Field.Bind(() => _booking.Name).Live(),
                    Ui.Field.Bind(() => _booking.Email).Live(),
                    Ui.Field.Bind(() => _booking.Seats).Live(),
                    Ui.Field.Bind(() => _booking.Plan).Live()
                ],
                Div.Class("mt-4 space-y-4")[
                    Ui.Field.Bind(() => _booking.Note).Multiline().Live(),
                    Ui.Field.Bind(() => _booking.Agreed).Label("I agree to the terms").Live()
                ]
            ],
            Ui.Text.Data("testid", "ui-field-auto-state")[
                $"{_booking.Name} · {_booking.Email} · {_booking.Seats.ToString(System.Globalization.CultureInfo.InvariantCulture)} seats"
                + $" · {_booking.Plan} · agreed {(_booking.Agreed ? "yes" : "no")}"
            ]
        ];

    public enum Plan
    {
        Starter,
        Team,
        Enterprise,
    }

    // An ordinary model: the attributes say what each member is, and the field reads them.
    public sealed class Booking
    {
        [Required]
        [Display(Name = "Name", Description = "As it is on your card.", Prompt = "Ada Lovelace")]
        public string Name { get; set; } = "";

        [EmailAddress]
        [Display(Name = "Email", Prompt = "ada@example.com")]
        public string? Email { get; set; }

        [Display(Name = "Seats")]
        public int Seats { get; set; } = 1;

        [Display(Name = "Plan")]
        public Plan Plan { get; set; } = Plan.Team;

        [Display(Name = "Note", Description = "Anything we should know.")]
        public string? Note { get; set; }

        public bool Agreed { get; set; }
    }
}
