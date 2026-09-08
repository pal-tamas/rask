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
/// </remarks>
public sealed partial class UiOtp : Component
{
    /// <summary>The accessible name — what the code is for.</summary>
    public required string Label { get; set; }

    /// <summary>How many characters. daisyUI draws the boxes from this.</summary>
    public required int Length { get; set; }

    public string? Value { get; set; }

    /// <summary>Runs the code up to the caller as it is typed.</summary>
    public Action<string>? OnChange { get; set; }

    /// <summary>Runs once the code is <see cref="Length" /> characters long.</summary>
    public Action<string>? OnComplete { get; set; }

    /// <summary>Draws the boxes joined into one block rather than separated.</summary>
    public bool? Joined { get; set; }

    public UiTone? Tone { get; set; }

    public UiSize? Size { get; set; }

    public string? Class { get; set; }

    /// <inheritdoc />
    protected override Component? Render()
    {
        var field = Input
            .Value(Value ?? string.Empty)
            .Type(InputType.Text)
            .Aria(new Dictionary<string, string?> { ["label"] = Label })
            .Class(UiClass.Compose(
                "otp",
                Joined == true ? "otp-joined" : "",
                Tone is { } tone ? UiClassNames.OtpTone(tone) : "",
                Size is { } size ? UiClassNames.OtpSize(size) : "",
                Class))
            .Attributes(
                // daisyUI counts the boxes from this, and it also stops a reader typing past the end.
                ("maxlength", Length.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                ("autocomplete", "one-time-code"),
                ("inputmode", "numeric"),
                ("pattern", "[0-9]*"));

        if (OnChange is null && OnComplete is null)
        {
            return field;
        }

        return field.OnChange(value =>
        {
            OnChange?.Invoke(value);

            // Fires on the transition INTO a complete code, not on every keystroke while it is
            // complete: a caller that submits from here would otherwise submit on every edit.
            if (OnComplete is { } complete && value.Length == Length && (Value?.Length ?? 0) != Length)
            {
                complete(value);
            }
        });
    }
}
