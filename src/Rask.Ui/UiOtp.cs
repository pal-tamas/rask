using System.Globalization;

namespace Rask;

/// <summary>
/// A short code, one character to a box.
/// </summary>
/// <remarks>
/// <para>
/// <b>One input, drawn as several.</b> The obvious build — one <c>&lt;input&gt;</c> per digit — is the
/// one to avoid: it needs script to move focus between the boxes, it defeats the browser's own SMS
/// autofill, and pasting a code lands the whole string in the first box. daisyUI's <c>otp</c> draws the
/// separators over a single field, so paste, autofill, backspace and select-all are the platform's.
/// </para>
/// <para>
/// <c>autocomplete="one-time-code"</c> and <c>inputmode="numeric"</c> are what tell a phone to offer
/// the code from the message it just received and to show the number pad; without them this is a text
/// box that happens to look like a code field.
/// </para>
/// <para>
/// A form control over a <c>string</c>, concretely rather than generically — a one-time code is
/// characters, and parsing one to a number would lose a leading zero.
/// <c>.Bind(() =&gt; model.Code)</c> two-way binds; <see cref="UiFormField{T}.Value" /> with <see cref="UiFormField{T}.OnChange" />
/// leaves it with the parent. <see cref="OnComplete" /> runs in both modes.
/// </para>
/// </remarks>
public sealed partial class UiOtp : UiFormField<string>
{
    // The completeness of the code at the LAST commit, so OnComplete can fire on the transition into a
    // full code rather than on every edit made while it is already full. An instance field because it is
    // the one thing neither mode's value tells us: in bound mode the model already holds the new value by
    // the time we are called.
    private bool _complete;

    /// <summary>How many characters. daisyUI draws the boxes from this.</summary>
    public required int Length { get; set; }

    /// <summary>Runs once the code first reaches <see cref="Length" /> characters.</summary>
    /// <remarks>
    ///     On the transition INTO a complete code, not on every keystroke while it is complete: a caller
    ///     that submits from here would otherwise submit on every edit.
    /// </remarks>
    public Callback<string> OnComplete { get; set; }

    /// <summary>Draws the boxes joined into one block rather than separated.</summary>
    public bool? Joined { get; set; }




    /// <inheritdoc />
    /// <inheritdoc />
    protected override Component Control()
    {
        // OnComplete rides the mode's own write-back rather than a second handler: AfterBind in bound
        // mode, OnChange in controlled. Wiring `oninput` as well would fire it mid-paste.
        if (Bind is { } bind)
        {
            return Input
                .Bind(bind)
                .Validate(Validate)
                // The consumer's hook runs first, then completion. This used to be two steps — the
                // consumer's on `AfterBind` and ours on `AfterBindAsync` — which worked only because both
                // ran. One slot means one handler, so ours calls theirs.
                .AfterBind(async value =>
                {
                    await AfterBind.Invoke(value).ConfigureAwait(false);
                    await CompleteAsync(value).ConfigureAwait(false);
                })
                .Type(InputType.Text)
                .Id(FieldId)
                .Disabled(Disabled == true)
                .Aria(ControlAria())
                .Class(BoxClass())
                .Attributes(Hints());
        }

        return Input
            .Value(Value ?? string.Empty)
            .OnChange(async value =>
            {
                await OnChange.Invoke(value).ConfigureAwait(false);
                await CompleteAsync(value).ConfigureAwait(false);
            })
            .Type(InputType.Text)
            .Id(FieldId)
            .Disabled(Disabled == true)
            .Aria(ControlAria())
            .Class(BoxClass())
            .Attributes(Hints());
    }

    private async Task CompleteAsync(string value)
    {
        var full = value.Length == Length;
        var crossed = full && !_complete;
        _complete = full;

        if (!crossed)
        {
            return;
        }

        await OnComplete.Invoke(value).ConfigureAwait(false);
    }

    private (string Name, string? Value)[] Hints() =>
    [
        // daisyUI counts the boxes from this, and it also stops a reader typing past the end.
        ("maxlength", Length.ToString(CultureInfo.InvariantCulture)),
        ("autocomplete", "one-time-code"),
        ("inputmode", "numeric"),
        ("pattern", "[0-9]*"),
    ];

    private string BoxClass() =>
        UiClass.Compose(
            "otp",
            Joined == true ? "otp-joined" : "",
            Tone is { } tone ? UiClassNames.OtpTone(tone) : "",
            Size is { } size ? UiClassNames.OtpSize(size) : "",
            Class);
}
