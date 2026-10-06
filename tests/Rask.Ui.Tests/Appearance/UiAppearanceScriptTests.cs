using System.Text.Json;
using Rask.TestSupport;

namespace Rask.UiTests.Appearance;

/// <summary>
///     What <c>Ui.AppearanceScript</c> does to <c>&lt;html&gt;</c> and <c>localStorage</c> — Flux's dark
///     mode contract.
/// </summary>
/// <remarks>
///     The script is run, not read: <c>AppearanceScriptFixture.ts</c> executes it under Node against a stub
///     document. Without Node these return early; <c>DarkModeTests</c> covers the same ground in a browser.
/// </remarks>
public sealed class UiAppearanceScriptTests
{
    private const string Key = "rask.appearance";

    [Theory]
    [InlineData("dark", false, true)]
    [InlineData("dark", true, true)]
    [InlineData("light", true, false)]
    [InlineData("light", false, false)]
    [InlineData("system", true, true)]
    [InlineData(null, true, true)]
    [InlineData(null, false, false)]
    public void The_stored_appearance_decides_and_without_one_the_operating_system_does(
        string? stored, bool osDark, bool dark)
    {
        var scenario = new { key = Key, stored, osDark, steps = Array.Empty<object>() };

        var page = Play(scenario);

        if (page is not { } html)
        {
            return;
        }

        Assert.Equal(dark, html.GetProperty("darkClass").GetBoolean());
        Assert.Equal(dark, html.GetProperty("dark").GetBoolean());
    }

    [Theory]
    [InlineData("dracula")]
    [InlineData("DARK")]
    [InlineData("dark ")]
    [InlineData("true")]
    [InlineData("")]
    public void Only_the_exact_words_are_a_choice(string stored)
    {
        var scenario = new { key = Key, stored, osDark = false, steps = Array.Empty<object>() };

        var page = Play(scenario);

        if (page is not { } html)
        {
            return;
        }

        Assert.False(html.GetProperty("darkClass").GetBoolean());
        Assert.Equal("system", html.GetProperty("appearance").GetString());
        Assert.Equal("light", html.GetProperty("dataTheme").GetString());
    }

    [Theory]
    [InlineData(true, "dark")]
    [InlineData(false, "light")]
    public void The_scheme_is_mirrored_to_data_theme_while_daisyUI_draws_the_kit(bool osDark, string theme)
    {
        var scenario = new { key = Key, stored = (string?)null, osDark, steps = Array.Empty<object>() };

        var page = Play(scenario);

        if (page is not { } html)
        {
            return;
        }

        Assert.Equal(theme, html.GetProperty("dataTheme").GetString());
    }

    [Fact]
    public void Setting_dark_stores_the_word_and_setting_system_stores_nothing()
    {
        var toDark = new { key = Key, stored = (string?)null, osDark = false, steps = new object[] { new { dark = true } } };
        var toSystem = new { key = Key, stored = "dark", osDark = false, steps = new object[] { new { appearance = "system" } } };

        var dark = Play(toDark);
        var system = Play(toSystem);

        if (dark is not { } darkened || system is not { } followed)
        {
            return;
        }

        Assert.Equal("dark", darkened.GetProperty("stored").GetString());
        Assert.True(darkened.GetProperty("darkClass").GetBoolean());
        Assert.Equal(JsonValueKind.Null, followed.GetProperty("stored").ValueKind);
        Assert.False(followed.GetProperty("darkClass").GetBoolean());
    }

    [Fact]
    public void Setting_an_unknown_appearance_means_system()
    {
        var scenario = new { key = Key, stored = "dark", osDark = false, steps = new object[] { new { appearance = "dracula" } } };

        var page = Play(scenario);

        if (page is not { } html)
        {
            return;
        }

        Assert.Equal("system", html.GetProperty("appearance").GetString());
        Assert.Equal(JsonValueKind.Null, html.GetProperty("stored").ValueKind);
    }

    [Fact]
    public void System_follows_the_operating_system_when_it_changes_and_a_choice_does_not()
    {
        var following = new { key = Key, stored = (string?)null, osDark = false, steps = new object[] { new { os = true } } };
        var chosen = new { key = Key, stored = "light", osDark = false, steps = new object[] { new { os = true } } };

        var followed = Play(following);
        var kept = Play(chosen);

        if (followed is not { } system || kept is not { } light)
        {
            return;
        }

        Assert.True(system.GetProperty("darkClass").GetBoolean());
        Assert.False(light.GetProperty("darkClass").GetBoolean());
    }

    [Fact]
    public void A_morph_that_strips_the_class_gets_it_back_and_the_earlier_hook_still_runs()
    {
        var scenario = new { key = Key, stored = "dark", osDark = false, steps = new object[] { new { morph = true } } };

        var page = Play(scenario);

        if (page is not { } html)
        {
            return;
        }

        Assert.True(html.GetProperty("darkClass").GetBoolean());
        Assert.Equal("dark", html.GetProperty("dataTheme").GetString());
        Assert.True(html.GetProperty("earlierHookRan").GetBoolean());
    }

    [Fact]
    public void Another_tab_changing_the_appearance_is_followed()
    {
        var scenario = new { key = Key, stored = (string?)null, osDark = false, steps = new object[] { new { otherTab = "dark" } } };

        var page = Play(scenario);

        if (page is not { } html)
        {
            return;
        }

        Assert.True(html.GetProperty("darkClass").GetBoolean());
        Assert.Equal("dark", html.GetProperty("appearance").GetString());
    }

    [Fact]
    public void The_toggle_still_works_when_storage_is_unavailable()
    {
        var scenario = new
        {
            key = Key,
            stored = (string?)null,
            osDark = false,
            storageThrows = true,
            steps = new object[] { new { dark = true } },
        };

        var page = Play(scenario);

        if (page is not { } html)
        {
            return;
        }

        Assert.True(html.GetProperty("darkClass").GetBoolean());
    }

    [Fact]
    public void A_storage_key_of_its_own_is_the_one_read()
    {
        var scenario = new { key = "shop.appearance", stored = "dark", osDark = false, steps = Array.Empty<object>() };

        var page = Play(scenario, UiAppearanceScript.Js("shop.appearance"));

        if (page is not { } html)
        {
            return;
        }

        Assert.True(html.GetProperty("darkClass").GetBoolean());
    }

    [Theory]
    [InlineData("a'b")]
    [InlineData("a\\b")]
    [InlineData("</script>")]
    [InlineData("")]
    public void A_storage_key_that_could_leave_its_string_literal_is_refused(string key)
    {
        var script = new UiAppearanceScript { StorageKey = key };

        var render = () => script.ToHtml();

        Assert.Throws<ArgumentException>(render);
    }

    [Fact]
    public void It_renders_one_inline_script_with_no_event_handler()
    {
        var script = new UiAppearanceScript();

        var html = script.ToHtml();

        Assert.StartsWith("<script>", html, StringComparison.Ordinal);
        Assert.EndsWith("</script>", html, StringComparison.Ordinal);
        Assert.DoesNotContain("data-rask-on", html, StringComparison.Ordinal);
    }

    private static JsonElement? Play(object scenario, string? script = null) =>
        NodeFixture.Run("AppearanceScriptFixture", script ?? UiAppearanceScript.Js(Key), JsonSerializer.Serialize(scenario));
}
