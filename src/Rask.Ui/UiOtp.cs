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
/// <c>Ui.OtpGroup</c> as children and it counts the cells itself.
/// </para>
/// <para>
/// The cells are the browser's while they are typed into: the runtime (<c>data-rask-otp</c>) moves on as a
/// character lands, walks back on Backspace, shares a pasted code out from the first cell, and keeps ONE hidden
/// field equal to the code. That field is what binds, so keys never wait for a round trip. Letters are
/// upper-cased in the value, as Flux's are; a cell goes on showing a letter in the case it was typed in, where
/// Flux's shows the capital — the runtime's hook does not change case.
/// </para>
/// </remarks>
public sealed partial class UiOtp : Component, IFormControl<string>, IUiFormControl
{
    // What the hidden field last reported, as it was typed. It is rendered back unchanged while it still spells
    // the code: the runtime tells its own value coming back from a new one by the exact text.
    private string? _typed;
    private string? _ownId;

    /// <inheritdoc cref="IUiFormControl.Label" />
    public string? Label { get; set; }

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

    /// <summary>The <c>name</c> the code posts under in a form.</summary>
    public string? Name { get; set; }

    /// <inheritdoc cref="UiInput{T}.Disabled" />
    public bool? Disabled { get; set; }

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

    string IUiFieldControl.ControlId => Id is null && Bind is null && Label is null
        ? _ownId ??= UiFieldId.Own(UiInstanceCounter.Next())
        : UiFieldId.Derive(Id, Bind, Label);

    string? IUiFormControl.Description => null;

    string? IUiFormControl.Badge => null;

    bool? IUiFormControl.Invalid => null;

    LambdaExpression? IUiFieldControl.Bound => Bind;

    /// <inheritdoc />
    protected override Component? Render()
    {
        var field = UiWithField.For(this);
        var (accessor, context, current) = UiFormCommit.Resolve(this);
        var cells = Children?.Any() == true ? Children.ToList() : Drawn();
        var total = Number(cells, 0);
        var mode = Mode ?? Ui.OtpMode.Numeric;
        var live = new Live(accessor, context, UiOtpCode.Filter(current, mode, total), mode, total);
        var scope = new UiOtpScope(
            live.Code.Length,
            total,
            mode,
            Private == true,
            Disabled == true,
            field.Invalid,
            Autocomplete ?? "one-time-code",
            field.ControlId);

        return field.Wrap(
            Div.Class(UiClass.Compose("flex items-center gap-2 w-fit", Class))
                .Data(("ui-otp", ""), ("ui-control", ""), ("rask-otp", Accepts(mode)))
                .Role("group")
                .Aria(Names(field))[
                Input.Value(Shown(live))
                    .OnChange(raw => TypedAsync(live, raw))
                    .Type(InputType.Hidden)
                    .Name(Name)
                    .Disabled(Disabled == true),
                Context.Provide(scope)[cells]
            ]);
    }

    private static string Accepts(Ui.OtpMode mode) => mode switch
    {
        Ui.OtpMode.Alpha => "alpha",
        Ui.OtpMode.Alphanumeric => "alphanumeric",
        _ => "",
    };

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

    private string Shown(Live live) =>
        _typed is { } typed && string.Equals(UiOtpCode.Filter(typed, live.Mode, live.Total), live.Code, StringComparison.Ordinal)
            ? typed
            : live.Code;

    private async Task TypedAsync(Live live, string raw)
    {
        _typed = raw;
        var code = UiOtpCode.Filter(raw, live.Mode, live.Total);
        if (string.Equals(code, live.Code, StringComparison.Ordinal))
        {
            return;
        }

        await UiFormCommit.CommitAsync(this, live.Accessor, live.Context, code).ConfigureAwait(false);
        if (code.Length == live.Total)
        {
            await OnComplete.Invoke(code).ConfigureAwait(false);
        }
    }

    // What one render resolved, for the handler it registered.
    private sealed record Live(ExpressionAccessor.Accessor? Accessor, EditContext? Context, string Code, Ui.OtpMode Mode, int Total);
}
