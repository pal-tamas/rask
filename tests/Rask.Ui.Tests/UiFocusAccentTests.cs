namespace Rask.UiTests;

/// <summary>
///     A FOCUSED FIELD (owner, 2026-11-03): a focused field draws no browser outline but its border and an INSET ring in
///     the app's accent, which no frame around it can clip; a button, tab, checkbox, radio or switch draws THE KEYBOARD
///     RING instead (UiKeyboardRingTests). Read from the COMPILED sheet.
/// </summary>
public sealed class UiFocusAccentTests
{
    private const string Ring = "box-shadow:inset0001pxvar(--color-accent)";

    private static readonly string[] Fields =
    [
        "input[data-ui-control]", "textarea[data-ui-control]", "select[data-ui-control]", "[data-ui-select-button]",
        "[data-ui-date-picker-button]", "[data-ui-time-picker-button]", "[data-ui-datetime-picker-button]",
        "[data-ui-color-picker-button]", "[data-ui-pillbox-trigger]", "[data-ui-color-picker-trigger]",
    ];

    private static readonly string[] RingedByTheKeyboardRing =
        ["[data-ui-button]", "[data-ui-tab]", "[data-ui-checkbox]", "[data-ui-radio]", "[data-ui-switch]", "[data-ui-navlist-item]"];

    [Fact]
    public void Every_field_and_trigger_is_ringed_in_the_accent_when_focused()
    {
        var rule = AccentRule();

        var selector = rule.Selector;

        Assert.Contains(":focus-visible", selector, StringComparison.Ordinal);
        Assert.All(Fields, field => Assert.Contains(field, selector, StringComparison.Ordinal));
        Assert.Contains("outline:none", rule.Body, StringComparison.Ordinal);
        Assert.Contains("border-color:var(--color-accent)", rule.Body, StringComparison.Ordinal);
    }

    [Fact]
    public void The_ring_is_drawn_inside_the_field_so_no_frame_around_it_clips_it()
    {
        var rule = AccentRule();

        var body = rule.Body;

        Assert.Contains(Ring, body, StringComparison.Ordinal);
        Assert.DoesNotContain("outline-offset", body, StringComparison.Ordinal);
        Assert.DoesNotContain("outline-width", body, StringComparison.Ordinal);
    }

    [Fact]
    public void A_box_or_a_tick_is_not_ringed_as_a_field()
    {
        var rule = AccentRule();

        var selector = rule.Selector;

        Assert.All(RingedByTheKeyboardRing, part => Assert.DoesNotContain(part, selector, StringComparison.Ordinal));
        Assert.Contains("[type=checkbox]", selector, StringComparison.Ordinal);
        Assert.Contains("[type=radio]", selector, StringComparison.Ordinal);
    }

    [Fact]
    public void An_invalid_field_stays_red_when_focused()
    {
        var rules = Rules();

        var red = rules.Where(r => r.Body.Contains("box-shadow:inset0001pxvar(--color-red-500)", StringComparison.Ordinal)).ToList();

        var only = Assert.Single(red);
        Assert.Contains("[data-invalid]", only.Selector, StringComparison.Ordinal);
        Assert.Contains("border-color:var(--color-red-500)", only.Body, StringComparison.Ordinal);
    }

    private static (string Selector, string Body) AccentRule() =>
        Assert.Single(Rules(), r => r.Body.Contains(Ring, StringComparison.Ordinal));

    private static List<(string Selector, string Body)> Rules() =>
        UiStylesheetTests.Rules(UiStylesheet.Css)
            .Select(r => (r.Selector, Body: r.Body.Replace(" ", "", StringComparison.Ordinal).Replace("\n", "", StringComparison.Ordinal)))
            .ToList();
}
