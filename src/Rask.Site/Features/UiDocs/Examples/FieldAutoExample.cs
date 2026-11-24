using System.ComponentModel.DataAnnotations;
using Rask.Core.Forms;

namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class FieldAutoExample : Component
{
    private readonly Booking _booking = new();

    protected override Component? Render() =>
        Div.Class("space-y-4")[
            Form.Model(_booking)[
                Div.Class("grid gap-4 sm:grid-cols-2")[
                    // Label, help text and placeholder come off [Display]; the badge off [Required]; maxlength off
                    // [StringLength]; the input type off [EmailAddress] and [Phone]; read-only off [Editable(false)].
                    Ui.Field.Bind(() => _booking.Name).Live(),
                    Ui.Field.Bind(() => _booking.Email).Live(),
                    Ui.Field.Bind(() => _booking.Phone).Live(),
                    Ui.Field.Bind(() => _booking.Reference),
                    Ui.Field.Bind(() => _booking.Seats).Live(),
                    Ui.Field.Bind(() => _booking.Plan).Live()
                ],
                Div.Class("mt-4 space-y-4")[
                    // The control picked and tuned by hand: the field still labels it from the member.
                    Ui.Field[Ui.Input.Bind(() => _booking.Plate).InputClass("uppercase").Live(), Ui.Error],
                    // A textarea off [DataType(DataType.MultilineText)], a switch off [UIHint(FieldHints.Switch)].
                    Ui.Field.Bind(() => _booking.Note).Live(),
                    Ui.Field.Bind(() => _booking.Newsletter).Live(),
                    Ui.Field.Bind(() => _booking.Agreed).Label("I agree to the terms").Live()
                ]
            ],
            Ui.Text.Data("testid", "ui-field-auto-state")[
                $"{_booking.Name} · {_booking.Email} · {_booking.Seats.ToString(System.Globalization.CultureInfo.InvariantCulture)} seats"
                + $" · plate {_booking.Plate}"
                + $" · {_booking.Plan} · newsletter {(_booking.Newsletter ? "yes" : "no")} · agreed {(_booking.Agreed ? "yes" : "no")}"
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
        [StringLength(60)]
        [Display(Name = "Name", Description = "As it is on your card.", Prompt = "Ada Lovelace")]
        public string Name { get; set; } = "";

        [EmailAddress]
        [Display(Name = "Email", Prompt = "ada@example.com")]
        public string? Email { get; set; }

        [Phone]
        [Display(Name = "Phone", Prompt = "+36 30 123 4567")]
        public string? Phone { get; set; }

        [Editable(false)]
        [Display(Name = "Booking reference")]
        public string Reference { get; set; } = "BK-1042";

        [Display(Name = "Seats")]
        public int Seats { get; set; } = 1;

        [Display(Name = "Plan")]
        public Plan Plan { get; set; } = Plan.Team;

        [Required]
        [Display(Name = "Licence plate", Description = "Shown in capitals.")]
        public string Plate { get; set; } = "";

        [DataType(DataType.MultilineText)]
        [Display(Name = "Note", Description = "Anything we should know.")]
        public string? Note { get; set; }

        [UIHint(FieldHints.Switch)]
        [Display(Name = "Send me the newsletter")]
        public bool Newsletter { get; set; }

        public bool Agreed { get; set; }
    }
}
