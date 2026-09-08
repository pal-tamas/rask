namespace Rask.Ui.Tests.Components;

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
    public void Joined_is_opt_in()
    {
        Assert.DoesNotContain("otp-joined", Otp());
        Assert.Contains("otp-joined",
            UiOtp.Value("").Label("Verification code").Length(6).Joined(true).ToHtml());
    }

    [Theory]
    [InlineData(UiTone.Primary, "otp-primary")]
    [InlineData(UiTone.Error, "otp-error")]
    public void Every_tone_writes_its_own_class(UiTone tone, string expected) =>
        Assert.Contains(expected, UiOtp.Value("").Label("Code").Length(6).Tone(tone).ToHtml());

    [Theory]
    [InlineData(UiSize.Xs, "otp-xs")]
    [InlineData(UiSize.Xl, "otp-xl")]
    public void Every_size_writes_its_own_class(UiSize size, string expected) =>
        Assert.Contains(expected, UiOtp.Value("").Label("Code").Length(6).Size(size).ToHtml());

    [Fact]
    public void The_current_value_is_rendered() =>
        Assert.Contains("1234", UiOtp.Value("1234").Label("Code").Length(6).ToHtml());

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

    private static string Otp() => UiOtp.Value("").Label("Verification code").Length(6).ToHtml();
}
