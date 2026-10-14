using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace Rask.UiTests.Components;

/// <summary>
///     Flux UI's phone field: the markup the phone engine takes over, and the files it ships as.
/// </summary>
/// <remarks>
///     What the field does while it is typed into — formatting, the country following the number, the list —
///     is the engine's, pinned key by key against Flux's live page in <c>tests/Rask.Server.E2E.Tests/UiPhoneTests</c>.
///     These hold the contract the component owes the engine and the runtime's hook (<c>data-rask-phone</c>).
/// </remarks>
public partial class UiPhoneTests : global::Rask.Core.RaskMarkup
{
    private static readonly string Resources = Path.Combine(RepoRoot.FullPath, "src", "Rask.Ui", "Resources");

    [Fact]
    public void The_number_is_a_tel_input_with_no_value_and_the_value_is_one_hidden_field()
    {
        var html = Ui.Phone.Value("+12015550123").Country("US").Name("phone").ToHtml().Decoded();

        Assert.Matches("<input[^>]*type=\"tel\"[^>]*>", html);
        Assert.DoesNotMatch("<input(?=[^>]*type=\"tel\")[^>]*value=", html);
        Assert.Contains("autocomplete=\"tel\"", html, StringComparison.Ordinal);
        Assert.Contains("inputmode=\"tel\"", html, StringComparison.Ordinal);
        Assert.Matches("<input(?=[^>]*type=\"hidden\")(?=[^>]*name=\"phone\")(?=[^>]*value=\"\\+12015550123\")", html);
    }

    [Fact]
    public void The_root_asks_for_the_engine_and_names_the_country_and_the_lists()
    {
        var html = Ui.Phone.Value("").Country("HU").Countries(["HU", "AT"]).CountryOrder(["AT"]).ToHtml().Decoded();

        Assert.Contains("data-rask-phone=\"/js/rask-ui-phone.js?v=" + UiPhoneEngine.Version + "\"", html, StringComparison.Ordinal);
        Assert.Contains("data-rask-phone-country=\"hu\"", html, StringComparison.Ordinal);
        Assert.Contains("data-rask-phone-countries=\"hu,at\"", html, StringComparison.Ordinal);
        Assert.Contains("data-rask-phone-order=\"at\"", html, StringComparison.Ordinal);
        Assert.Contains("data-ui-phone", html, StringComparison.Ordinal);
    }

    [Fact]
    public void The_button_shows_the_starting_country_and_says_it_in_its_name()
    {
        var html = Ui.Phone.Value("").Country("HU").ToHtml().Decoded();

        Assert.Contains("aria-label=\"Change country: Hungary (+36)\"", html, StringComparison.Ordinal);
        Assert.Contains("data-country-label=\"Change country\"", html, StringComparison.Ordinal);
        Assert.Contains("data-country=\"HU\"", html, StringComparison.Ordinal);
        Assert.Contains(">+36</span>", html, StringComparison.Ordinal);
        // The chosen country's row is drawn before the list opens, picked, as Flux draws it.
        Assert.Matches("<div(?=[^>]*role=\"option\")(?=[^>]*data-selected)(?=[^>]*aria-selected=\"true\")[^>]*value=\"hu\"", html);
    }

    [Fact]
    public void Every_flag_the_picker_fills_carries_the_phones_marker()
    {
        var html = Ui.Phone.Value("").Country("HU").ToHtml().Decoded();

        var marked = Regex.Matches(html, "data-ui-phone-country-flag").Count;

        // The button's copy, its template, the row's template and the chosen country's row.
        Assert.Equal(4, marked);
    }

    [Fact]
    public void With_no_country_the_button_shows_the_globe_and_its_name_alone()
    {
        var html = Ui.Phone.Value("").ToHtml().Decoded();

        Assert.Contains("aria-label=\"Change country\"", html, StringComparison.Ordinal);
        Assert.Contains("data-ui-select-placeholder", html, StringComparison.Ordinal);
        Assert.DoesNotContain("data-rask-phone-country=", html, StringComparison.Ordinal);
    }

    [Fact]
    public void The_list_carries_no_handler_so_opening_and_searching_it_send_nothing()
    {
        var html = Ui.Phone.Value("").Country("US").ToHtml().Decoded();

        Assert.DoesNotContain("data-rask-on-", html.Replace("data-rask-on-change", "", StringComparison.Ordinal), StringComparison.Ordinal);
        Assert.Contains("popover=\"auto\"", html, StringComparison.Ordinal);
        Assert.Contains("popovertarget=", html, StringComparison.Ordinal);
        Assert.Contains("placeholder=\"Search...\"", html, StringComparison.Ordinal);
        Assert.Contains("No results found", html, StringComparison.Ordinal);
        Assert.Contains("data-ui-phone-option", html, StringComparison.Ordinal);
    }

    [Fact]
    public void A_read_only_field_says_so_on_its_button_and_its_list_does_not_open()
    {
        var html = Ui.Phone.Value("+12015550123").Country("US").ReadOnly().ToHtml().Decoded();

        Assert.Contains("aria-readonly=\"true\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("popovertarget=", html, StringComparison.Ordinal);
        Assert.Matches("<input(?=[^>]*type=\"tel\")[^>]*readonly", html);
    }

    [Fact]
    public void An_invalid_field_marks_its_root_and_its_number()
    {
        var html = Ui.Phone.Value("201").Country("US").Invalid().ToHtml().Decoded();

        Assert.Matches("data-ui-phone(?=[^>]*data-invalid)[^>]*aria-invalid=\"true\"|aria-invalid=\"true\"[^>]*data-ui-phone", html);
        Assert.Matches("<input(?=[^>]*type=\"tel\")[^>]*data-invalid", html);
    }

    [Fact]
    public void Every_country_of_the_engine_is_known_with_its_calling_code_in_English_order()
    {
        var all = UiPhoneCountries.All;

        Assert.Equal(244, all.Length);
        Assert.Equal(("AF", "93", "Afghanistan"), (all[0].Code, all[0].DialCode, all[0].Name));
        Assert.Equal(("AX", "Åland Islands"), (all[1].Code, all[1].Name));
        Assert.Contains(all, country => country is { Code: "HU", DialCode: "36", Name: "Hungary" });
    }

    [Fact]
    public void The_engines_versions_are_the_hashes_of_the_committed_files()
    {
        var engine = File.ReadAllBytes(Path.Combine(Resources, "ui-phone.js"));
        var utils = File.ReadAllBytes(Path.Combine(Resources, "ui-phone-utils.js"));

        var engineHash = Convert.ToHexStringLower(SHA256.HashData(engine))[..8];
        var utilsHash = Convert.ToHexStringLower(SHA256.HashData(utils))[..8];

        Assert.Equal(UiPhoneEngine.Version, engineHash);
        Assert.Equal(UiPhoneEngine.UtilsVersion, utilsHash);
        Assert.Contains("rask-ui-phone-utils.js?v=" + utilsHash, File.ReadAllText(Path.Combine(Resources, "ui-phone.js")), StringComparison.Ordinal);
    }

    [Fact]
    public void The_engine_travels_with_the_MIT_notice_of_intl_tel_input_and_the_Apache_licence_of_its_utils()
    {
        var notices = File.ReadAllText(Path.Combine(Resources, "ui-phone.LICENSES.txt"));
        var banner = File.ReadLines(Path.Combine(Resources, "ui-phone.js")).First();
        var lockfile = File.ReadAllText(Path.Combine(Resources, "phone", "package-lock.json"));

        Assert.Contains("intl-tel-input 29.2.3 — MIT", notices, StringComparison.Ordinal);
        Assert.Contains("Copyright (c) 2014-2016 Jack O'Connor", notices, StringComparison.Ordinal);
        Assert.Contains("Apache License", notices, StringComparison.Ordinal);
        Assert.Contains("Version 2.0, January 2004", notices, StringComparison.Ordinal);
        Assert.Contains("rask-ui-phone.LICENSES.txt", banner, StringComparison.Ordinal);
        Assert.Matches("\"node_modules/intl-tel-input\": \\{\\s*\"version\": \"29.2.3\",[^}]*\"integrity\": \"sha512-", lockfile);
    }

    [Fact]
    public void An_app_gets_the_phone_engine_and_its_flags_with_no_switch()
    {
        var targets = File.ReadAllText(Path.Combine(RepoRoot.FullPath, "src", "Rask.Ui", "build", "Rask.Ui.targets"));

        var byDefault = Regex.Match(targets, "<RaskUiPhoneEngine Condition=\"([^\"]+)\">true</RaskUiPhoneEngine>").Groups[1].Value;

        Assert.Contains("'$(UsingMicrosoftNETSdkWeb)' == 'true'", byDefault, StringComparison.Ordinal);
        Assert.Contains("'$(UsingMicrosoftNETSdkWebAssembly)' == 'true'", byDefault, StringComparison.Ordinal);
        Assert.Contains("'$(RaskUiWriteFlags)' == '' AND '@(_RaskUiPhoneKit)' != ''", targets, StringComparison.Ordinal);
    }
}

internal static class UiPhoneHtml
{
    /// <summary>The markup with its character references read: the serializer writes a plus as <c>&amp;#x2B;</c>.</summary>
    public static string Decoded(this string html) => System.Net.WebUtility.HtmlDecode(html);
}
