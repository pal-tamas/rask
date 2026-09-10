using System.Globalization;
using System.Linq.Expressions;
using Rask.Core.Forms;

namespace Rask.Ui;

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
/// <c>.Bind(() =&gt; model.Code)</c> two-way binds; <see cref="Value" /> with <see cref="OnChange" />
/// leaves it with the parent. <see cref="OnComplete" /> runs in both modes.
/// </para>
/// </remarks>
public sealed partial class UiOtp : Component, IFormControl<string>
{
    // The completeness of the code at the LAST commit, so OnComplete can fire on the transition into a
    // full code rather than on every edit made while it is already full. An instance field because it is
    // the one thing neither mode's value tells us: in bound mode the model already holds the new value by
    // the time we are called.
    private bool _complete;

    /// <summary>The accessible name — what the code is for.</summary>
    public required string Label { get; set; }

    /// <summary>How many characters. daisyUI draws the boxes from this.</summary>
    public required int Length { get; set; }

    /// <summary>Runs once the code first reaches <see cref="Length" /> characters.</summary>
    /// <remarks>
    ///     On the transition INTO a complete code, not on every keystroke while it is complete: a caller
    ///     that submits from here would otherwise submit on every edit.
    /// </remarks>
    public Callback<string>? OnComplete { get; set; }

    /// <summary>Draws the boxes joined into one block rather than separated.</summary>
    public bool? Joined { get; set; }

    public UiTone? Tone { get; set; }

    public UiSize? Size { get; set; }

    public string? Class { get; set; }

    /// <inheritdoc />
    public string? Value { get; set; }

    /// <inheritdoc />
    public Callback<string>? OnChange { get; set; }


    /// <inheritdoc />
    public Expression<Func<string>>? Bind { get; set; }

    /// <inheritdoc />
    public Validator<string>? Validate { get; set; }


    /// <inheritdoc />
    public Callback<string>? AfterBind { get; set; }


    /// <inheritdoc />
    protected override Component? Render()
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
                    if (AfterBind?.Invoke(value) is { } hook)
                    {
                        await hook.ConfigureAwait(false);
                    }

                    await CompleteAsync(value).ConfigureAwait(false);
                })
                .Type(InputType.Text)
                .Aria(Aria())
                .Class(BoxClass())
                .Attributes(Hints());
        }

        return Input
            .Value(Value ?? string.Empty)
            .OnChange(async value =>
            {
                if (OnChange?.Invoke(value) is { } notify)
                {
                    await notify.ConfigureAwait(false);
                }

                await CompleteAsync(value).ConfigureAwait(false);
            })
            .Type(InputType.Text)
            .Aria(Aria())
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

        if (OnComplete?.Invoke(value) is { } complete)
        {
            await complete.ConfigureAwait(false);
        }
    }

    private (string Name, string? Value)[] Hints() =>
    [
        // daisyUI counts the boxes from this, and it also stops a reader typing past the end.
        ("maxlength", Length.ToString(CultureInfo.InvariantCulture)),
        ("autocomplete", "one-time-code"),
        ("inputmode", "numeric"),
        ("pattern", "[0-9]*"),
    ];

    // aria-invalid is OMITTED rather than nulled — a null renders the attribute valueless, and a
    // valueless aria-invalid reads as "true", which would mark every field in the kit invalid.
    private Dictionary<string, string?> Aria() =>
        Tone == UiTone.Error
            ? new Dictionary<string, string?> { ["label"] = Label, ["invalid"] = "true" }
            : new Dictionary<string, string?> { ["label"] = Label };

    private string BoxClass() =>
        UiClass.Compose(
            "otp",
            Joined == true ? "otp-joined" : "",
            Tone is { } tone ? UiClassNames.OtpTone(tone) : "",
            Size is { } size ? UiClassNames.OtpSize(size) : "",
            Class);
}
