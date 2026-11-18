using Rask.Core;

namespace Rask.UiTests.Components;

/// <summary>
///     <see cref="UiFacts" /> and <see cref="Ui.Fact" />: a summary card's description list, each label over its value in
///     Flux's typography.
/// </summary>
public partial class UiFactsTests : RaskMarkup
{
    [Fact]
    public void Facts_are_a_description_list_whose_every_fact_groups_its_term_over_its_value()
    {
        var facts = Ui.Facts[Ui.Fact("Company", "Aminoff Freight"), Ui.Fact("Depots", 4)];

        var html = facts.ToHtml().AsText();

        Assert.Equal(
            "<dl class=\"[:where(&)]:grid [:where(&)]:gap-x-6 [:where(&)]:gap-y-4 [:where(&)]:m-0 [:where(&)]:grid-cols-[repeat(auto-fit,minmax(min(100%,12rem),1fr))]\" data-ui-facts>"
            + "<div data-ui-fact><dt class=\"text-xs text-zinc-400 dark:text-white/50\">Company</dt>"
            + "<dd class=\"m-0 mt-0.5 min-w-0 break-words text-sm text-zinc-800 dark:text-white\">Aminoff Freight</dd></div>"
            + "<div data-ui-fact><dt class=\"text-xs text-zinc-400 dark:text-white/50\">Depots</dt>"
            + "<dd class=\"m-0 mt-0.5 min-w-0 break-words text-sm text-zinc-800 dark:text-white\">4</dd></div></dl>",
            html);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void A_fact_with_no_value_shows_a_dash_under_its_label(string? value)
    {
        var fact = Ui.Fact("Driver card", value);

        var html = fact.ToHtml().AsText();

        Assert.EndsWith(">–</dd></div>", html, StringComparison.Ordinal);
    }

    [Fact]
    public void A_fact_s_value_is_any_markup_and_its_words_are_encoded()
    {
        var badge = Ui.Fact("Status", Ui.Badge.Color(Ui.Color.Lime)["Active"]).ToHtml();
        var words = Ui.Fact("Note", "<b>& co</b>").ToHtml();

        Assert.Contains("data-ui-badge", badge, StringComparison.Ordinal);
        Assert.Contains("&lt;b&gt;&amp; co&lt;/b&gt;", words, StringComparison.Ordinal);
    }

    [Fact]
    public void The_call_site_s_columns_win_and_a_fact_can_span_them()
    {
        var facts = Ui.Facts.Class("sm:grid-cols-3")[Ui.Fact("Note", "Long").Class("col-span-full")];

        var html = facts.ToHtml().AsText();

        Assert.StartsWith("<dl class=\"[:where(&)]:grid [:where(&)]:gap-x-6 [:where(&)]:gap-y-4 [:where(&)]:m-0 [:where(&)]:grid-cols-[repeat(auto-fit,minmax(min(100%,12rem),1fr))] sm:grid-cols-3\"", html, StringComparison.Ordinal);
        Assert.Contains("<div class=\"col-span-full\" data-ui-fact>", html, StringComparison.Ordinal);
    }

    [Fact]
    public void A_fact_refuses_a_missing_label()
    {
        string label = null!;

        var act = () => Ui.Fact(label, "x");

        Assert.Throws<ArgumentNullException>(act);
    }
}
