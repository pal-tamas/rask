namespace Rask.UiTests.Components;

/// <summary>
///     The one-time code field, which is one input drawn as several.
/// </summary>
public partial class UiOtpTests : global::Rask.Core.RaskMarkup
{
    [Fact]
    public void It_is_a_single_input_rather_than_one_box_per_character()
    {
        // The obvious build is one <input> per digit, and it is the one to avoid: it needs script to
        // move focus between boxes, it defeats the browser's SMS autofill, and pasting a code lands the
        // whole string in the first box. daisyUI draws the separators over one field.
        var html = Otp();

        Assert.Equal(1, Occurrences(html, "<input"));
        Assert.Contains("class=\"otp", html);
    }

    [Fact]
    public void The_length_reaches_the_browser_as_well_as_daisyUI()
    {
        // daisyUI counts the boxes from maxlength, and the browser uses the same attribute to stop a
        // reader typing past the end. One attribute, both jobs.
        Assert.Contains("maxlength=\"6\"", Otp());
    }

    [Fact]
    public void It_asks_the_phone_for_the_code_it_just_received()
    {
        // Without these it is a text box that happens to look like a code field: no autofill from the
        // message, and a full keyboard instead of the number pad.
        var html = Otp();

        Assert.Contains("autocomplete=\"one-time-code\"", html);
        Assert.Contains("inputmode=\"numeric\"", html);
    }

    [Fact]
    public void It_names_itself() =>
        Assert.Contains("aria-label=\"Verification code\"", Otp());

    [Fact]
    public void A_visible_label_names_it_and_a_hint_describes_it()
    {
        // #1117: the kit's field shape — a <label for> rather than a second, invisible name, and the hint tied
        // to the input a screen reader lands on.
        var html = Ui.Otp.Value("").Length(6).Label("Verification code").Hint("Sent to your phone").ToHtml();

        Assert.Matches("<label [^>]*for=\"f-verification-code\"", html);
        Assert.Contains("id=\"f-verification-code\"", html);
        Assert.DoesNotContain("aria-label=", html);
        Assert.Contains("aria-describedby=\"f-verification-code-hint\"", html);
    }

    [Fact]
    public void An_error_tone_marks_it_invalid_and_a_badge_rides_the_label()
    {
        var html = Ui.Otp.Value("").Length(6).Label("Code").Badge("Required").Tone(Ui.Tone.Error).ToHtml();

        Assert.Contains("aria-invalid=\"true\"", html);
        Assert.Contains("Required", html);
    }

    [Fact]
    public void Joined_is_opt_in()
    {
        Assert.DoesNotContain("otp-joined", Otp());
        Assert.Contains("otp-joined",
            Ui.Otp.Value("").Length(6).AccessibleLabel("Verification code").Joined(true).ToHtml());
    }

    [Theory]
    [InlineData(Ui.Tone.Primary, "otp-primary")]
    [InlineData(Ui.Tone.Error, "otp-error")]
    public void Every_tone_writes_its_own_class(Ui.Tone tone, string expected) =>
        Assert.Contains(expected, Ui.Otp.Value("").Length(6).AccessibleLabel("Code").Tone(tone).ToHtml());

    [Theory]
    [InlineData(Ui.Size.Xs, "otp-xs")]
    [InlineData(Ui.Size.Xl, "otp-xl")]
    public void Every_size_writes_its_own_class(Ui.Size size, string expected) =>
        Assert.Contains(expected, Ui.Otp.Value("").Length(6).AccessibleLabel("Code").Size(size).ToHtml());

    [Fact]
    public void The_current_value_is_rendered() =>
        Assert.Contains("1234", Ui.Otp.Value("1234").Length(6).AccessibleLabel("Code").ToHtml());

    private static int Occurrences(string haystack, string needle)
    {
        var count = 0;
        var at = 0;
        while ((at = haystack.IndexOf(needle, at, StringComparison.Ordinal)) >= 0)
        {
            count++;
            at += needle.Length;
        }

        return count;
    }

    private static string Otp() => Ui.Otp.Value("").Length(6).AccessibleLabel("Verification code").ToHtml();
}
