namespace Rask.Bootstrap.Tests;

// PROTOTYPE — the builder setters on the Bootstrap layer, which is the library that exercises the
// INTERMEDIATE-base case: a Bs component inherits Id/Class from BsBlock and the shared field props
// (Label/Disabled/Required/Size/HelpText/Name/Floating) from BsFormControl<T>, neither of which is part
// of the Element/Component chain the generator writes once as constrained generic extensions. Those
// props used to get no setter at all — the chain simply would not compile — so these pin that a Bs
// control can be built end to end without falling back to the factory.
public class BsBuilderSetterTests
{
    [Fact]
    public void A_prop_inherited_from_BsBlock_gets_a_setter() =>
        Assert.Equal(
            "<span id=\"tag\" class=\"badge text-bg-success\">New</span>",
            BsBadge().Id("tag").Color(BsColor.Success)["New"].ToHtml());

    // The receiver stays the concrete component, so an inherited setter can be chained into an own
    // one and back — a BsBlock-typed extension would return the base and end the chain.
    [Fact]
    public void An_inherited_setter_chains_with_the_components_own() =>
        Assert.Equal(
            "<button class=\"btn btn-primary btn-lg w-100\" type=\"button\">Save</button>",
            BsButton().Color(BsColor.Primary).Class("w-100").Size(BsSize.Lg)["Save"].ToHtml());

    // BsFormControl<T> sits between the control and BsBlock, so its props are two levels up — and the
    // control is generic, so the setter carries the type parameter through.
    [Fact]
    public void A_prop_inherited_from_BsFormControl_gets_a_setter()
    {
        var model = new CheckModel();

        var html = BsCheck(() => model.Done).Label("Agree").Disabled(true).ToHtml();

        Assert.Contains("form-check-input", html, StringComparison.Ordinal);
        Assert.Contains("Agree", html, StringComparison.Ordinal);
        Assert.Contains("disabled", html, StringComparison.Ordinal);
    }

    private sealed class CheckModel
    {
        public bool Done { get; set; }
    }
}
