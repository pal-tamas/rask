using System.Linq.Expressions;
using Rask.Core.Forms;

namespace Rask;

/// <summary>
/// Flux UI's OTP input: <c>Ui.Otp.Bind(() =&gt; m.Code).Length(6)</c>, one character to a cell.
/// </summary>
/// <remarks>
/// <para>
/// A form control over a <c>string</c> — a code is characters, and a number would lose a leading zero.
/// <c>.Bind(() =&gt; model.Code)</c> two-way binds and drives the form's validation; <c>.Value(code)</c> with
/// <c>OnChange</c> leaves it with the parent. <see cref="OnComplete" /> runs when the last cell is filled, which
/// is where Flux's <c>submit="auto"</c> submits.
/// </para>
/// <para>
/// <see cref="Length" /> draws that many cells; or place <c>Ui.OtpInput</c>, <c>Ui.OtpSeparator</c> and
/// <c>Ui.OtpGroup</c> as children and it counts the cells itself. Each cell is a real text input. The code is
/// kept without gaps, held to the <see cref="Mode" />'s characters with letters upper-cased, and text of several
/// characters in one cell — the code typed straight through, a paste, a code offered by the phone — is the code
/// from that cell on.
/// </para>
/// </remarks>
public sealed partial class UiOtp : Component, IFormControl<string>, IUiFormControl
{
    // How often each cell has been drawn afresh, and which cells hold the rest of the code: see Settled and
    // UiOtpCode.
    private readonly Dictionary<int, int> _redrawn = [];
    private readonly HashSet<int> _runsOn = [];

    /// <inheritdoc cref="IUiFormControl.Label" />
    public string? Label { get; set; }

    /// <inheritdoc cref="IUiFormControl.Description" />
    public string? Description { get; set; }

    /// <inheritdoc cref="IUiFormControl.DescriptionTrailing" />
    public string? DescriptionTrailing { get; set; }

    /// <summary>How many cells to draw. Ignored when the cells are placed as children.</summary>
    public int? Length { get; set; }

    /// <summary>Which characters a cell takes. Digits unless set.</summary>
    public Ui.OtpMode? Mode { get; set; }

    /// <summary>Masks what is typed, as a password input does.</summary>
    public bool? Private { get; set; }

    /// <summary>The first cell's <c>autocomplete</c>. <c>one-time-code</c> unless set; <c>off</c> to stop the browser offering one.</summary>
    public string? Autocomplete { get; set; }

    /// <inheritdoc cref="UiInput{T}.Disabled" />
    public bool? Disabled { get; set; }

    /// <inheritdoc cref="UiInput{T}.Invalid" />
    public bool? Invalid { get; set; }

    /// <summary>Classes for the row of cells: <c>mx-auto</c>.</summary>
    public string? Class { get; set; }

    /// <inheritdoc cref="Element.Id" />
    public string? Id { get; set; }

    /// <inheritdoc cref="IUiFormControl.ShowValidation" />
    public bool? ShowValidation { get; set; }

    /// <inheritdoc />
    public string? Value { get; set; }

    /// <inheritdoc />
    public Callback<string> OnChange { get; set; }

    /// <inheritdoc />
    public Expression<Func<string>>? Bind { get; set; }

    /// <inheritdoc />
    public Validator<string>? Validate { get; set; }

    /// <inheritdoc />
    public Callback<string> AfterBind { get; set; }

    /// <summary>Runs with the code when its last cell is filled.</summary>
    public Callback<string> OnComplete { get; set; }

    string IUiFieldControl.ControlId => UiFieldId.Derive(Id, Bind, Label);

    string? IUiFormControl.Badge => null;

    LambdaExpression? IUiFieldControl.Bound => Bind;

    /// <inheritdoc />
    protected override Component? Render()
    {
        var field = UiWithField.For(this);
        var (accessor, context, current) = UiFormCommit.Resolve(this);
        var cells = Children?.Any() == true ? Children.ToList() : Drawn();
        var total = Number(cells, 0);
        var mode = Mode ?? Ui.OtpMode.Numeric;
        var live = new Live(accessor, context, UiOtpCode.Filter(current, mode, total), total);
        var scope = new UiOtpScope(
            live.Code,
            total,
            mode,
            Private == true,
            Disabled == true,
            field.Invalid,
            Autocomplete ?? "one-time-code",
            field.ControlId,
            _redrawn,
            (cell, raw) => TypedAsync(live, cell, raw),
            (cell, raw) => Settled(live, cell, raw));

        return field.Wrap(
            Div.Class(UiClass.Compose("flex items-center gap-2 w-fit", Class))
                // A code is a secret for as long as it works: only the first cell says one-time-code, so the whole
                // row is kept out of what the runtime carries across a reload.
                .Data(("ui-otp", ""), ("ui-control", ""), ("rask-no-restore", ""))
                .Role("group")
                .Aria(Names(field))[
                Context.Provide(scope)[cells]
            ]);
    }

    private List<Component?> Drawn() =>
        [.. Enumerable.Range(0, Math.Max(Length ?? 0, 0)).Select(Component? (_) => Ui.OtpInput)];

    // Each cell is told which one it is, in the order they are written — through groups too.
    private static int Number(IEnumerable<Component?> parts, int next)
    {
        foreach (var part in parts)
        {
            if (part is UiOtpInput cell)
            {
                cell.Cell = next++;
            }
            else if (part is UiOtpGroup { Children: { } grouped })
            {
                next = Number(grouped, next);
            }
        }

        return next;
    }

    // The row is a group named by the field's label; each cell names itself.
    private static IReadOnlyDictionary<string, string?> Names(UiWithField field)
    {
        if (field.LabelledBy is not { } label)
        {
            return field.Aria;
        }

        return new Dictionary<string, string?>(field.Aria, StringComparer.Ordinal) { ["labelledby"] = label };
    }

    private async Task TypedAsync(Live live, int cell, string raw)
    {
        var (typed, runsOn) = UiOtpCode.Typed(live.Code, cell, raw, Mode ?? Ui.OtpMode.Numeric, live.Total, _runsOn.Contains(cell));
        if (runsOn)
        {
            _runsOn.Add(cell);
        }

        if (string.Equals(typed, live.Code, StringComparison.Ordinal))
        {
            return;
        }

        await UiFormCommit.CommitAsync(this, live.Accessor, live.Context, typed).ConfigureAwait(false);
        if (typed.Length == live.Total)
        {
            await OnComplete.Invoke(typed).ConfigureAwait(false);
        }
    }

    // Focus has left a cell. A browser keeps what was typed in the input that has focus, whatever is rendered
    // for it meanwhile — so a cell left showing text the code did not keep (a refused character, a whole code
    // typed or pasted into it, the gap a deleted character closed) is replaced by a new element now.
    private void Settled(Live live, int cell, string raw)
    {
        _runsOn.Remove(cell);
        if (!string.Equals(cell < live.Code.Length ? live.Code[cell].ToString() : "", raw, StringComparison.Ordinal))
        {
            _redrawn[cell] = _redrawn.GetValueOrDefault(cell) + 1;
        }
    }

    // What one render resolved, for the handlers it registered.
    private sealed record Live(ExpressionAccessor.Accessor? Accessor, EditContext? Context, string Code, int Total);
}
