using Rask.Core.Components;
using Rask.Core.Tests.Live;

namespace Rask.Core.Tests.Components;

public class ButtonTests
{
    [Fact]
    public void Render_NullProps_ReturnsEmptyButtonTags() =>
        Assert.Equal("<button></button>", new Button().ToHtml());

    [Fact]
    public void Render_DisabledTrue_EmitsBareDisabledAttribute()
    {
        Assert.Equal(
            "<button disabled></button>",
            new Button { Disabled = true }.ToHtml());
    }

    [Fact]
    public void Render_DisabledFalse_OmitsDisabledAttribute()
    {
        Assert.Equal(
            "<button></button>",
            new Button { Disabled = false }.ToHtml());
    }

    [Fact]
    public void Render_TypeSet_EmitsTypeAttribute()
    {
        Assert.Equal(
            "<button type=\"submit\"></button>",
            new Button { Type = "submit" }.ToHtml());
    }

    [Fact]
    public void Render_NameAndValue_EmitsBothQuoted()
    {
        Assert.Equal(
            "<button name=\"action\" value=\"save\"></button>",
            new Button { Name = "action", Value = "save" }.ToHtml());
    }

    [Fact]
    public void Render_AllPropsSet_EmitsBaseThenDerivedAttributesInOrder()
    {
        
        Assert.Equal(
            "<button id=\"go\" class=\"btn\" style=\"color:red\" data-test-id=\"primary\" type=\"submit\" disabled name=\"action\" value=\"save\"></button>",
            new Button { Type = "submit", Disabled = true, Name = "action", Value = "save", Id = "go", Class = "btn", Style = "color:red", Data = new Dictionary<string, string?> { ["test-id"] = "primary" } }.ToHtml());
    }

    [Fact]
    public void Render_StringChild_EncodesText()
    {
        Assert.Equal(
            "<button>&lt;click&gt;</button>",
            new Button { Children = ["<click>"] }.ToHtml());
    }

    [Fact]
    public void Render_RawChild_RendersVerbatim()
    {
        Assert.Equal(
            "<button><i>!</i></button>",
            new Button { Children = [new Raw("<i>!</i>")] }.ToHtml());
    }

    [Fact]
    public void Constructor_ParamsArray_RendersChildrenInOrder()
    {
        Assert.Equal(
            "<button>a<b></button>",
            new Button { Children = ["a", new Raw("<b>")] }.ToHtml());
    }

    [Fact]
    public void Constructor_IEnumerableOverload_RendersChildrenInOrder()
    {
        var children = new List<Child> { "a", new Raw("<b>") };
        Assert.Equal(
            "<button>a<b></button>",
            new Button { Children = children }.ToHtml());
    }

    [Fact]
    public void Render_OnClickOutsideLiveContext_OmitsHandlerAttribute()
    {
                Assert.Equal("<button></button>", new Button { OnClick = () => { } }.ToHtml());
    }

    [Fact]
    public void Render_OnClickInsideLiveContext_EmitsDataRaskOnClick()
    {
        var view = new StubComponent(() => new Button { OnClick = () => { }, Children = ["x"] });
        Assert.Equal(
            "<button data-rask-on-click=\"h0\">x</button>",
            view.RenderAsLiveRoot());
    }

    [Fact]
    public void Render_OnClickAsyncInsideLiveContext_EmitsDataRaskOnClick()
    {
        var view = new StubComponent(() => new Button { OnClickAsync = async () => { await Task.Yield(); }, Children = ["x"] });
        Assert.Equal(
            "<button data-rask-on-click=\"h0\">x</button>",
            view.RenderAsLiveRoot());
    }
}
