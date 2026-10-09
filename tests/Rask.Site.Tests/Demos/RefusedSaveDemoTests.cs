using Rask.Site.Tests.Infrastructure;

namespace Rask.Site.Tests.Demos;

/// <summary>
///     The "database said no" demo, without a browser: a name the store already holds is refused under the
///     field, and a different one saves.
/// </summary>
public sealed partial class RefusedSaveDemoTests : global::Rask.Core.RaskMarkup
{
    private const string Refusal = "A route with this name already exists.";

    [Fact]
    public async Task A_name_the_store_holds_is_refused_under_the_field_and_nothing_is_saved()
    {
        var page = Page.Render(() => RefusedSaveDemo, TestServices.Default());

        await page.On("#v13-name").Input("Budapest – Wien");
        await page.On("form").Submit();

        Assert.Contains(Refusal, page.TextOf("#v13-name-error"), StringComparison.Ordinal);
        Assert.Equal("true", page.Find("#v13-name").Attribute("aria-invalid"));
        Assert.DoesNotContain("Saved:", page.Html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_empty_name_is_stopped_by_the_fields_own_rule_and_drawn_the_same_way()
    {
        var page = Page.Render(() => RefusedSaveDemo, TestServices.Default());

        await page.On("form").Submit();

        Assert.Contains("A route needs a name.", page.TextOf("#v13-name-error"), StringComparison.Ordinal);
        Assert.Equal("true", page.Find("#v13-name").Attribute("aria-invalid"));
    }

    [Fact]
    public async Task Changing_the_name_takes_the_refusal_away_and_the_next_save_goes_through()
    {
        var page = Page.Render(() => RefusedSaveDemo, TestServices.Default());
        await page.On("#v13-name").Input("Budapest – Wien");
        await page.On("form").Submit();

        await page.On("#v13-name").Input("Wien – Graz");
        var corrected = page.Html;
        await page.On("form").Submit();

        Assert.DoesNotContain(Refusal, corrected, StringComparison.Ordinal);
        Assert.Contains("Saved: Wien", page.Html, StringComparison.Ordinal);
    }
}
