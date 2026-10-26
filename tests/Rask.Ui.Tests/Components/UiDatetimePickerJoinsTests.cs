using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;

namespace Rask.UiTests.Components;

/// <summary>
///     What a datetime picker's button writes between a moment's date and its time, by the picker's locale — Unicode
///     CLDR's, as the browser's Intl writes it on Flux's (measured 2026-10-26: <c>Oct 29, 2026 at 2:00 PM</c>,
///     <c>29 oct. 2026 à 14:00</c>, <c>2026年10月29日 14:00</c>).
/// </summary>
public sealed partial class UiDatetimePickerJoinsTests : global::Rask.Core.RaskMarkup
{
    private static readonly DateTime Moment = new(2026, 10, 29, 14, 0, 0);

    private static CultureInfo In(string name) => CultureInfo.GetCultureInfo(name);

    [Theory]
    [InlineData("en", " at ")]
    [InlineData("fr", " à ")]
    [InlineData("de", " um ")]
    public void A_locale_the_data_names_joins_with_its_own_word(string locale, string word)
    {
        var culture = In(locale);

        var join = UiDatetimePickerJoins.Of(culture);

        Assert.Equal(word, join);
    }

    [Theory]
    [InlineData("fr-CA", " à ")]
    [InlineData("de-AT", " um ")]
    [InlineData("pt-BR", " às ")]
    public void A_region_the_data_does_not_name_joins_as_its_language(string locale, string word)
    {
        var culture = In(locale);

        var join = UiDatetimePickerJoins.Of(culture);

        Assert.Equal(word, join);
    }

    [Fact]
    public void A_language_the_data_does_not_name_joins_as_the_root_locale()
    {
        var culture = CultureInfo.InvariantCulture;

        var join = UiDatetimePickerJoins.Of(culture);

        Assert.Equal("{1} {0}", join);
    }

    [Theory]
    [InlineData("ja", "{1} {0}")]
    [InlineData("hu", "{1} {0}")]
    [InlineData("es", "{1}, {0}")]
    [InlineData("ru", "{1}, {0}")]
    public void A_locale_with_no_one_word_between_writes_the_whole_moment_in_a_pattern(string locale, string pattern)
    {
        var culture = In(locale);

        var join = UiDatetimePickerJoins.Of(culture);

        Assert.True(UiDatetimePickerJoins.IsPattern(join));
        Assert.Equal(pattern, join);
    }

    [Theory]
    [InlineData("en-US", "Oct 29, 2026<span class=\"text-zinc-400\"> at </span>2:00 PM")]
    [InlineData("fr", "29 oct. 2026<span class=\"text-zinc-400\"> à </span>14:00")]
    [InlineData("de", "29. Okt. 2026<span class=\"text-zinc-400\"> um </span>14:00")]
    [InlineData("ja", "2026年10月29日 14:00")]
    [InlineData("hu", "2026. okt. 29. 14:00")]
    public void The_button_writes_the_moment_as_its_locale_joins_it(string locale, string written)
    {
        var picker = Ui.DatetimePicker.Value(Moment).Locale(locale);

        var html = picker.ToHtml();

        Assert.Equal(written, WebUtility.HtmlDecode(Regex.Match(html, "dir=\"auto\">(.*?)</div>", RegexOptions.None, TimeSpan.FromSeconds(1)).Groups[1].Value));
    }

    [Fact]
    public void The_browser_is_handed_the_join_of_the_pickers_locale_alone()
    {
        var picker = Ui.DatetimePicker.Value(Moment).Locale("fr");

        var html = picker.ToHtml();

        Assert.Contains("&quot;join&quot;:&quot;", html, StringComparison.Ordinal);
        Assert.DoesNotContain(" um ", html, StringComparison.Ordinal);
    }
}
