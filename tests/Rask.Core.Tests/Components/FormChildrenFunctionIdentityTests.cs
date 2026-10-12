#pragma warning disable RASK014 // test-defined Component subclasses have no generated factories

namespace Rask.Core.Tests.Components;

// A form's children FUNCTION (`Form.Model(m)[f => [ … ]]`) runs during the walk, not during its owner's
// Render(), so what it builds is numbered on from the owner's last real render. An owner served from the
// render cache used to run it anyway with that counter never reset — every unrelated render of the page
// built the function's fields afresh, with fresh handler ids, and an event the browser had already read
// off the old markup landed on an id that no longer existed. The typed value was dropped without a word
// (the site's submit-state demo saved "" and its journey flaked).
internal sealed class SignupModel
{
    public string Name { get; set; } = "";
}

// The page: a sibling that re-renders on its own, beside a panel that stays clean.
internal sealed partial class SignupPage : Component
{
    internal readonly SignupModel Model = new();

    protected override Component? Render() => Div[Ticker, SignupPanel.Model(Model)];
}

internal sealed partial class Ticker : Component
{
    private int _ticks;

    protected override Component? Render() => Button.Id("tick").OnClick(() => _ticks++)[$"{_ticks}"];
}

internal sealed partial class SignupPanel : Component
{
    public required SignupModel Model { get; set; }

    protected override Component? Render() =>
        Form.Model(Model)[f => [NameField.Model(Model).Disabled(f.Submitting)]];
}

// A field that is a component of its own, as every kit field is: its handlers are numbered on ITS slots, so
// they are only as stable as the instance is.
internal sealed partial class NameField : Component
{
    public required SignupModel Model { get; set; }

    public bool Disabled { get; set; }

    protected override Component? Render() => Input.Bind(() => Model.Name).Id("name").Disabled(Disabled);
}

public class FormChildrenFunctionIdentityTests
{
    [Fact]
    public async Task A_field_built_by_the_submit_state_function_keeps_its_handler_when_the_page_renders_around_it()
    {
        var signup = new SignupPage();
        var page = Page.Render(signup);
        var typedInto = page.HandlerIdFor("#name", "change");

        await page.On("#tick").Click();
        await page.On("#tick").Click();
        var landed = await page.TryInvoke(typedInto, "{\"value\":\"ada\"}");

        Assert.True(landed, "the field's bind handler went stale when a sibling re-rendered");
        Assert.Equal("ada", signup.Model.Name);
        Assert.Equal(typedInto, page.HandlerIdFor("#name", "change"));
    }
}
