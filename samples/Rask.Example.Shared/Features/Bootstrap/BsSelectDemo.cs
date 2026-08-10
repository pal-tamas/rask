namespace Rask.Example.Shared.Features;

// Every BsSelect<T> variant side by side, each bound to one model and echoed by a live readout OUTSIDE the
// Form (a bound write re-renders the expression's owner, so the readout tracks each pick with no
// StateHasChanged). BsSelect renders a custom .dropdown-menu combobox by default; Filter adds an in-dropdown
// search field; a nullable value type gets an × that clears it; OptionValue binds a projected field while the
// options stay whole objects; Native drops to the OS <select>; Floating wraps the label like a form-floating.
public sealed partial class BsSelectDemo : Component
{
    private static readonly string[] Plans = ["free", "pro", "team"];
    private static readonly int?[] Seats = [1, 2, 5, 10];
    private static readonly Team[] Teams = [new(1, "Platform"), new(2, "Growth"), new(3, "Data")];

    private readonly Model _m = new();

    private static string PlanLabel(string p) => p switch
    {
        "free" => "Free",
        "pro" => "Pro",
        "team" => "Team",
        _ => p,
    };

    private static string SeatLabel(int? n) => $"{n} seat{(n == 1 ? "" : "s")}";

    // Groups the teams under .dropdown-header (custom) / <optgroup> (native) sections.
    private static string Division(Team t) => t.Id == 2 ? "Business" : "Engineering";

    protected override Component? Render() =>
    [
        Form<Model>(_m, Class: "vstack gap-3")[
            // 1. Basic — binds the chosen option itself; a muted placeholder shows when empty.
            BsSelect(() => _m.Plan, Plans, OptionLabel: p => Text.Value(PlanLabel(p)),
                Placeholder: "— choose —", Label: "Plan (basic)", Id: "sel-plan"),
            // 2. Floating — the label rides inside the box and floats up on focus / when filled.
            BsSelect(() => _m.PlanFloat, Plans, OptionLabel: p => Text.Value(PlanLabel(p)),
                Label: "Plan (floating)", Floating: true, Id: "sel-plan-float"),
            // 3. Searchable — a Filter predicate adds a search field that narrows the options.
            BsSelect(() => _m.PlanSearch, Plans, OptionLabel: p => Text.Value(PlanLabel(p)),
                Filter: (p, t) => PlanLabel(p).Contains(t, StringComparison.OrdinalIgnoreCase),
                Placeholder: "Search a plan…", Label: "Plan (searchable)", Id: "sel-plan-search"),
            // 4. Clearable — a nullable int? gets an × that resets it to null (the Placeholder state).
            BsSelect(() => _m.Seats, Seats, OptionLabel: n => Text.Value(SeatLabel(n)),
                Placeholder: "Any", Label: "Seats (nullable, clearable)", Id: "sel-seats"),
            // 5. Value selector — options are Team objects, but the bound value is the projected id.
            BsSelect(() => _m.TeamId, Options: Teams, OptionValue: t => t.Id, OptionLabel: t => Text.Value(t.Name),
                Filter: (t, q) => t.Name.Contains(q, StringComparison.OrdinalIgnoreCase),
                Placeholder: "No team", Label: "Team (binds to id)", Id: "sel-team"),
            // 6. Native — the OS <select>; handy on mobile.
            BsSelect(() => _m.Tier, Plans, OptionLabel: p => Text.Value(PlanLabel(p)), Native: true,
                Label: "Tier (native)", Id: "sel-tier"),
            // 7. Native + nullable — the leading empty option is a selectable "None" that round-trips null.
            BsSelect(() => _m.NativeSeats, Seats, OptionLabel: n => Text.Value(SeatLabel(n)), Native: true,
                Placeholder: "None", Label: "Seats (native, nullable)", Id: "sel-nseats"),
            // 8. Disabled — non-interactive, still shows its bound value.
            BsSelect(() => _m.Locked, Plans, OptionLabel: p => Text.Value(PlanLabel(p)),
                Label: "Plan (disabled)", Disabled: true, Id: "sel-locked"),
            // 9. Grouped + per-option disabled — OptionGroup renders .dropdown-header sections; OptionDisabled
            //    greys a non-selectable option that the keyboard cursor skips over.
            BsSelect(() => _m.GroupedTeamId, Options: Teams, OptionValue: t => t.Id, OptionLabel: t => Text.Value(t.Name),
                OptionGroup: Division, OptionDisabled: t => t.Id == 3,
                Placeholder: "Pick a team", Label: "Team (grouped, one disabled)", Id: "sel-grouped")
        ],
        BsAlert.Color(BsColor.Secondary).Class("mt-3 mb-0")[
            Span.Id("sel-readout")[
                $"Plan: {(_m.Plan is "" ? "—" : PlanLabel(_m.Plan))} · " +
                $"Floating: {PlanLabel(_m.PlanFloat)} · " +
                $"Search: {(_m.PlanSearch is "" ? "—" : PlanLabel(_m.PlanSearch))} · " +
                $"Seats: {(_m.Seats is { } s ? SeatLabel(s) : "—")} · " +
                $"Team: {(_m.TeamId is { } id ? Teams.First(t => t.Id == id).Name : "—")} · " +
                $"Tier: {PlanLabel(_m.Tier)} · " +
                $"NativeSeats: {(_m.NativeSeats is { } ns ? SeatLabel(ns) : "—")} · " +
                $"GroupedTeam: {(_m.GroupedTeamId is { } gt ? Teams.First(t => t.Id == gt).Name : "—")}"
            ]
        ]
    ];

    private sealed class Model
    {
        public string Plan { get; set; } = "";
        public string PlanFloat { get; set; } = "pro";
        public string PlanSearch { get; set; } = "";
        public int? Seats { get; set; }
        public int? TeamId { get; set; }
        public string Tier { get; set; } = "free";
        public int? NativeSeats { get; set; }
        public string Locked { get; set; } = "team";
        public int? GroupedTeamId { get; set; }
    }

    private sealed record Team(int Id, string Name);
}
