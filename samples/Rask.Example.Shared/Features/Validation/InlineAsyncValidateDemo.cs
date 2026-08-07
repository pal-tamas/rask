using Rask.Core.Forms;

namespace Rask.Example.Shared.Features;

public sealed partial class InlineAsyncValidateDemo : Component
{
    // Showcases the typed async Validate overload: a bare `async (v, ct) => …` lambda binds
    // directly to Func<TProp, CancellationToken, ValueTask<IEnumerable<string>>> on the Input,
    // and a bare `async (m, ct) => …` lambda binds the same shape on Form — both with no cast.
    // The 250ms delay drives the latest-wins cancellation path (rapid typing supersedes the
    // prior in-flight run) and ValidatingIndicator surfaces the pending state.
    private static readonly HashSet<string> TakenCodes =
        new(StringComparer.OrdinalIgnoreCase) { "BAD-001", "DEAD-BEEF", "RESERVED" };

    private readonly PromoModel _model = new();
    private string? _submission;

    private static Component FieldError(IReadOnlyList<string> msgs) =>
        [.. msgs.Select((m, i) => Div(Key: i, Class: "text-danger small mt-1")[m])];

    private static Component Checking() =>
        Span(Class: "validating-indicator text-muted small mt-1")[
            BsIcon(Name: BsIconName.ArrowClockwise, Class: "me-1"), "Checking…"
        ];

    private static Component? SummaryAlert(IReadOnlyList<ValidationEntry> entries)
    {
        var formOnly = entries.Where(e => e.Field.Length == 0).ToList();
        if (formOnly.Count == 0)
        {
            return null;
        }

        return BsAlert(Color: BsColor.Danger, Class: "small mb-0")[
            Ul(Class: "mb-0 ps-3")[formOnly.Select((e, i) => Li(Key: i)[e.Message])]
        ];
    }

    private static async ValueTask<IEnumerable<string>> CheckCodeAsync(string code, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return Array.Empty<string>();
        }

        await Task.Delay(250, ct).ConfigureAwait(false);
        return TakenCodes.Contains(code) ? new[] { $"\"{code}\" is reserved." } : Array.Empty<string>();
    }

    protected override Component? Render() =>
    [
        Form<PromoModel>(
            _model,
            OnValidSubmit: m => _submission = $"Redeemed: {m.Code}",
            Class: "vstack gap-3",
            Validate: async (m, ct) =>
            {
                await Task.Yield();
                ct.ThrowIfCancellationRequested();
                return string.IsNullOrWhiteSpace(m.Code)
                    ? new[] { "Code is required." }
                    : Array.Empty<string>();
            })[
            Div()[
                Label("v10-code", Class: "form-label small mb-1")["Promo code"],
                Input(() => _model.Code, Id: "v10-code", Class: "form-control",
                    Validate: CheckCodeAsync),
                ValidatingIndicator(() => _model.Code, Checking),
                ValidationMessage(() => _model.Code, FieldError)
            ],
            ValidationSummary(SummaryAlert),
            Div()[
                BsButton(Type: "submit", Color: BsColor.Primary)[BsIcon(Name: BsIconName.Gift, Class: "me-1"), "Redeem"]
            ]
        ],
        _submission is null
            ? null
            : BsAlert(Color: BsColor.Success, Class: "small mt-3 mb-0")[BsIcon(Name: BsIconName.CheckCircle, Class: "me-2"), _submission]
    ];
}

public sealed class PromoModel
{
    public string Code { get; set; } = "";
}
