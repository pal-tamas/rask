using Rask.Core.Forms;

namespace Rask.Site.Features;

public sealed partial class InlineAsyncValidateDemo : Component
{
    // Showcases the async Validate overload: a method group or a bare `async m => …` lambda binds
    // the asynchronous rule on the Input and on the Form alike — no cast, no …Async sibling. The
    // field's token is Current.Cancellation.
    // The field is Live, so the rule runs as the code is typed. The 250ms delay drives the
    // latest-wins cancellation path (typing on supersedes the run in flight), and the kit field
    // shows "Checking…" for the pending state on its own.
    private static readonly HashSet<string> TakenCodes =
        new(StringComparer.OrdinalIgnoreCase) { "BAD-001", "DEAD-BEEF", "RESERVED" };

    private readonly PromoModel _model = new();
    private string? _submission;

    private static Component? SummaryAlert(IReadOnlyList<ValidationEntry> entries)
    {
        var formOnly = entries.Where(e => e.Field.Length == 0).ToList();
        if (formOnly.Count == 0)
        {
            return null;
        }

        return Ui.Callout.Danger.Role("alert")[Ui.CalloutText[Ul.Class("mb-0 ps-3")[formOnly.Select((e, i) => Li.Key(i)[e.Message])]]];
    }

    private static async ValueTask<IEnumerable<string>> CheckCodeAsync(string code)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return Array.Empty<string>();
        }

        await Task.Delay(250, Current.Cancellation).ConfigureAwait(false);
        return TakenCodes.Contains(code) ? new[] { $"\"{code}\" is reserved." } : Array.Empty<string>();
    }

    protected override Component? Render() =>
    [
        Form.Model(_model).OnSubmit(m => _submission = $"Redeemed: {m.Code}").Class("flex flex-col gap-3").Validate(async m =>
            {
                await Task.Yield();
                Current.Cancellation.ThrowIfCancellationRequested();
                return string.IsNullOrWhiteSpace(m.Code)
                    ? ["Code is required."]
                    : [];
            })[
            Ui.Input.Bind(() => _model.Code).Live().Label("Promo code")
                .Id("v10-code")
                .Validate(CheckCodeAsync),
            Validation.Summary.Template(SummaryAlert),
            Div[
                Ui.Button.Primary.Icon(Ui.IconName.Gift).Submit["Redeem"]
            ]
        ],
        _submission is null
            ? null
            : Ui.Callout.Success.Icon(Ui.IconName.CheckCircle).Class("mt-3").Role("status").Text(_submission)
    ];
}
