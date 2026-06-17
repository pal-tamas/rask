#pragma warning disable RASK014 // test-defined Component subclasses have no generated factories

namespace Rask.Core.Tests.Components;

public class ButtonTests
{
    [Fact]
    public void Render_NullProps_ReturnsEmptyButtonTags() =>
        Assert.Equal("<button></button>", Button().ToHtml());

    [Fact]
    public void Render_DisabledTrue_EmitsBareDisabledAttribute()
    {
        Assert.Equal(
            "<button disabled></button>",
            Button(Disabled: true).ToHtml());
    }

    [Fact]
    public void Render_DisabledFalse_OmitsDisabledAttribute()
    {
        Assert.Equal(
            "<button></button>",
            Button(Disabled: false).ToHtml());
    }

    [Fact]
    public void Render_TypeSet_EmitsTypeAttribute()
    {
        Assert.Equal(
            "<button type=\"submit\"></button>",
            Button("submit").ToHtml());
    }

    [Fact]
    public void Render_NameAndValue_EmitsBothQuoted()
    {
        Assert.Equal(
            "<button name=\"action\" value=\"save\"></button>",
            Button(Name: "action", Value: "save").ToHtml());
    }

    [Fact]
    public void Render_AllPropsSet_EmitsBaseThenDerivedAttributesInOrder()
    {
        Assert.Equal(
            "<button id=\"go\" class=\"btn\" style=\"color:red\" data-test-id=\"primary\" type=\"submit\" disabled name=\"action\" value=\"save\"></button>",
            Button("submit", true, "action", "save", Id: "go", Class: "btn", Style: "color:red",
                Data: new Dictionary<string, string?> { ["test-id"] = "primary" }).ToHtml());
    }

    [Fact]
    public void Render_AccessibilityProps_PrecedeTagSpecificAttributes()
    {
        Assert.Equal(
            "<button data-test-id=\"x\" role=\"button\" tabindex=\"0\" aria-pressed=\"true\" type=\"submit\" disabled></button>",
            Button("submit", true, Data: new Dictionary<string, string?> { ["test-id"] = "x" },
                Role: "button", TabIndex: 0,
                Aria: new Dictionary<string, string?> { ["pressed"] = "true" }).ToHtml());
    }

    [Fact]
    public void Render_StringChild_EncodesText()
    {
        Assert.Equal(
            "<button>&lt;click&gt;</button>",
            Button()["<click>"].ToHtml());
    }

    [Fact]
    public void Render_RawChild_RendersVerbatim()
    {
        Assert.Equal(
            "<button><i>!</i></button>",
            Button()[Raw("<i>!</i>")].ToHtml());
    }

    [Fact]
    public void Constructor_ParamsArray_RendersChildrenInOrder()
    {
        Assert.Equal(
            "<button>a<b></button>",
            Button()["a", Raw("<b>")].ToHtml());
    }

    [Fact]
    public void Constructor_IEnumerableOverload_RendersChildrenInOrder()
    {
        var children = new List<Child> { "a", Raw("<b>") };
        Assert.Equal(
            "<button>a<b></button>",
            Button()[children].ToHtml());
    }

    [Fact]
    public void Render_OnClickOutsideLiveContext_OmitsHandlerAttribute() =>
        Assert.Equal("<button></button>", Button(OnClick: () => { }).ToHtml());

    [Fact]
    public void Render_OnClickInsideLiveContext_EmitsDataRaskOnClick()
    {
        var view = new StubComponent(() => Button(OnClick: () => { })["x"]);
        Assert.Equal(
            "<button data-rask-on-click=\"h0\">x</button>",
            view.RenderAsLiveRoot());
    }

    [Fact]
    public void Render_OnClickAsyncInsideLiveContext_EmitsDataRaskOnClick()
    {
        var view = new StubComponent(() => Button(OnClickAsync: async () => { await Task.Yield(); })["x"]);
        Assert.Equal(
            "<button data-rask-on-click=\"h0\">x</button>",
            view.RenderAsLiveRoot());
    }
}
