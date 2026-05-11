using Rask.Core.Components;
using Rask.Core.Tests.Live;

namespace Rask.Core.Tests.Components;

public class ButtonTests
{
    [Fact]
    public void Render_NullProps_ReturnsEmptyButtonTags() =>
        Assert.Equal("<button></button>", new Button(null).ToHtml());

    [Fact]
    public void Render_DisabledTrue_EmitsBareDisabledAttribute()
    {
        Assert.Equal(
            "<button disabled></button>",
            new Button(new Button.Props(Disabled: true)).ToHtml());
    }

    [Fact]
    public void Render_DisabledFalse_OmitsDisabledAttribute()
    {
        Assert.Equal(
            "<button></button>",
            new Button(new Button.Props(Disabled: false)).ToHtml());
    }

    [Fact]
    public void Render_TypeSet_EmitsTypeAttribute()
    {
        Assert.Equal(
            "<button type=\"submit\"></button>",
            new Button(new Button.Props("submit")).ToHtml());
    }

    [Fact]
    public void Render_NameAndValue_EmitsBothQuoted()
    {
        Assert.Equal(
            "<button name=\"action\" value=\"save\"></button>",
            new Button(new Button.Props(Name: "action", Value: "save")).ToHtml());
    }

    [Fact]
    public void Render_AllPropsSet_EmitsBaseThenDerivedAttributesInOrder()
    {
        var props = new Button.Props(
            "submit",
            true,
            "action",
            "save",
            Id: "go",
            Class: "btn",
            Style: "color:red",
            Data: new Dictionary<string, string?> { ["test-id"] = "primary" });

        Assert.Equal(
            "<button id=\"go\" class=\"btn\" style=\"color:red\" data-test-id=\"primary\" type=\"submit\" disabled name=\"action\" value=\"save\"></button>",
            new Button(props).ToHtml());
    }

    [Fact]
    public void Render_StringChild_EncodesText()
    {
        Assert.Equal(
            "<button>&lt;click&gt;</button>",
            new Button(null, "<click>").ToHtml());
    }

    [Fact]
    public void Render_RawChild_RendersVerbatim()
    {
        Assert.Equal(
            "<button><i>!</i></button>",
            new Button(null, new Raw("<i>!</i>")).ToHtml());
    }

    [Fact]
    public void Constructor_ParamsArray_RendersChildrenInOrder()
    {
        Assert.Equal(
            "<button>a<b></button>",
            new Button(null, "a", new Raw("<b>")).ToHtml());
    }

    [Fact]
    public void Constructor_IEnumerableOverload_RendersChildrenInOrder()
    {
        var children = new List<Child> { "a", new Raw("<b>") };
        Assert.Equal(
            "<button>a<b></button>",
            new Button(null, children).ToHtml());
    }

    [Fact]
    public void Render_OnClickOutsideLiveContext_OmitsHandlerAttribute()
    {
        var props = new Button.Props(OnClick: () => { });
        Assert.Equal("<button></button>", new Button(props).ToHtml());
    }

    [Fact]
    public void Render_OnClickInsideLiveContext_EmitsDataRaskOnClick()
    {
        var view = new StubComponent(() => new Button(new Button.Props(OnClick: () => { }), "x"));
        Assert.Equal(
            "<button data-rask-on-click=\"h0\">x</button>",
            view.RenderAsLiveRoot());
    }

    [Fact]
    public void Render_OnClickAsyncInsideLiveContext_EmitsDataRaskOnClick()
    {
        var view = new StubComponent(() => new Button(
            new Button.Props(OnClickAsync: async () => { await Task.Yield(); }),
            "x"));
        Assert.Equal(
            "<button data-rask-on-click=\"h0\">x</button>",
            view.RenderAsLiveRoot());
    }
}
