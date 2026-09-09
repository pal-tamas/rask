namespace Rask.Ui.Tests.Components;

/// <summary>
///     UiSearch, which is a form control like every other field in the kit.
/// </summary>
/// <remarks>
///     It used to carry a single bespoke <c>OnSearch</c>, which made it the one field here that could not
///     be bound or validated — and, because that callback was wired to the change event, the one that
///     could not drive a filter that narrows as you type.
/// </remarks>
public partial class UiSearchTests : global::Rask.Core.RaskMarkup
{
    [Fact]
    public void Renders_the_leading_icon_and_the_field()
    {
        var html = UiSearch.Value("").Placeholder("Search keys").AccessibleLabel("Search cache keys").ToHtml();

        Assert.Contains("<svg", html);
        Assert.Contains("placeholder=\"Search keys\"", html);

        // The accessible name is separate from the placeholder on purpose: a placeholder disappears the
        // moment typing starts, taking the field's only label with it.
        Assert.Contains("aria-label=\"Search cache keys\"", html);
    }

    [Fact]
    public void Is_a_search_input()
    {
        var html = UiSearch.Value("").Placeholder("Find").AccessibleLabel("Find things").ToHtml();
        Assert.Contains("type=\"search\"", html);
    }

    [Fact]
    public void Controlled_mode_renders_the_value_it_is_given()
    {
        var html = UiSearch.Value("wake lock").Placeholder("Find").AccessibleLabel("Find things").ToHtml();
        Assert.Contains("value=\"wake lock\"", html);
    }

    [Fact]
    public void OnInput_registers_an_input_handler_and_OnChange_a_change_one()
    {
        // The two moments a search box has. OnInput fires per keystroke (an in-page filter); OnChange
        // fires on blur or Enter (a page that navigates on search). A filter wired to the change event
        // looks broken - you type a query and nothing happens until focus leaves the field - which is
        // the defect that made the showcase's sidebar hand-roll this control instead of using it.
        // Through RaskTest.Render, not ToHtml: a handler is only registered inside a LIVE render, so a
        // static render of either chain emits no data-rask-on-* attribute at all and the assertion would
        // pass or fail for the wrong reason.
        var live = global::Rask.Testing.RaskTest.Render(
            UiSearch.Value("").Placeholder("Find").AccessibleLabel("Find things").OnInput(_ => { })).Html;
        Assert.Contains("data-rask-on-input", live);

        var commit = global::Rask.Testing.RaskTest.Render(
            UiSearch.Value("").Placeholder("Find").AccessibleLabel("Find things").OnChange(_ => { })).Html;
        Assert.Contains("data-rask-on-change", commit);
    }

    [Fact]
    public void Block_drops_the_fixed_column_so_it_fills_a_narrow_rail()
    {
        // The default settles at 288px from sm up, which overflows a 256px sidebar. This cannot be
        // corrected from the call site with Class: both are `sm:` utilities, and which wins is decided
        // by stylesheet order rather than by their order in the class attribute.
        var standard = UiSearch.Value("").Placeholder("Find").AccessibleLabel("Find things").ToHtml();
        Assert.Contains("sm:w-72", standard);

        var block = UiSearch.Value("").Placeholder("Find").AccessibleLabel("Find things").Block(true).ToHtml();
        Assert.DoesNotContain("sm:w-72", block);
        Assert.Contains("w-full", block);
    }

    [Theory]
    [InlineData(UiSize.Xs, "input-xs")]
    [InlineData(UiSize.Sm, "input-sm")]
    public void Takes_a_size(UiSize size, string expected) =>
        Assert.Contains(
            expected,
            UiSearch.Value("").Placeholder("Find").AccessibleLabel("Find things").Size(size).ToHtml());
}
