using Rask.Core.Routing;

namespace Rask.Core.Tests.Components;

// The point of the portable chrome vocabulary: ONE Screen subclass, naming no Rask.Native type, that renders
// real markup on the web heads and is projected to platform bars inside a native shell (asserted from the
// other end in Rask.Native.Tests.Session.PortableChromeTests, against a class of the same shape).
//
// Until this landed, Screen's chrome slots were read on the native host only, and the only components that
// could fill them lived in Rask.Native — so a shared Screen forced a web app to reference the native package,
// and the slots rendered nothing there anyway.
public partial class ScreenChromeOnWebTests : global::Rask.Core.RaskMarkup
{
    [Fact]
    public void Screen_RendersItsAppBar_AsAWebLandmark()
    {
        var html = PortableScreen.ToHtml();

        Assert.Contains("""<div class="rask-header-bar" role="banner">""", html, StringComparison.Ordinal);
        Assert.Contains("""<div class="rask-header-title">Todos</div>""", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Screen_RendersItsTabStrip_AsANavigationLandmark() =>
        Assert.Contains("""<div class="rask-tab-bar" role="navigation">""", PortableScreen.ToHtml(),
            StringComparison.Ordinal);

    // Reading order is the accessibility contract, and it is the reason the tab bar is emitted after the body
    // on the web while the native collector still sees it before (where order is the deepest-wins merge rule,
    // not layout).
    [Fact]
    public void Screen_PutsTheHeaderBeforeTheBody_AndTheTabsAfterIt()
    {
        var html = PortableScreen.ToHtml();

        var header = html.IndexOf("rask-header-bar", StringComparison.Ordinal);
        var body = html.IndexOf("the-body", StringComparison.Ordinal);
        var tabs = html.IndexOf("rask-tab-bar", StringComparison.Ordinal);

        Assert.True(header >= 0 && body >= 0 && tabs >= 0, "all three regions render");
        Assert.True(header < body, "the header bar comes before the body");
        Assert.True(body < tabs, "the tab bar comes after the body");
    }

    [Fact]
    public void Screen_WithNoChrome_RendersOnlyItsBody() =>
        Assert.Equal("<p>plain</p>", PlainScreen.ToHtml());
}

// Names only Rask.Core components — this is the class that is supposed to serve every host.
internal sealed partial class PortableScreen : Screen
{
    protected override string Route => "/portable-screen";

    protected override Component? HeaderBar => AppBar.Title("Todos");

    protected override Component? TabBar =>
        TabStrip.Tabs([
            TabItem.Title("Home").Icon(BarIcon.Home).To(new RouteUrl("/")),
            TabItem.Title("Todos").Icon(BarIcon.List).To(new RouteUrl("/todos")),
        ]);

    protected override Component? Render() => P["the-body"];
}

// A screen that declares no chrome still costs nothing: the slots are null, so walking them adds no markup.
internal sealed partial class PlainScreen : Screen
{
    protected override string Route => "/plain-screen";

    protected override Component? Render() => P["plain"];
}
