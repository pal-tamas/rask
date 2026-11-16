using Rask.Core;
using Rask.Core.Routing;
using Page = Rask.Testing.Page;

namespace Rask.UiTests.Live;

/// <summary>
///     A layout that shows the page's title, over a page that declares one: the title settles in a second walk of the
///     first render, and the press after it is still answered with a diff (#1303).
/// </summary>
public class FirstPressAfterATitleTests
{
    [Fact]
    public async Task The_first_press_after_a_page_title_settles_is_a_diff_like_every_later_one()
    {
        var page = Page.Visit("/first-press-after-a-title/child");

        await page.Click("Go");
        await page.Click("Go");
        await page.Click("Go");

        Assert.Equal("Asked:True Kid", page.TextOf("#asked"));
        Assert.Empty(DiffGuardAttribute.FullPages());
        Assert.Equal(3, DiffGuardAttribute.Frames().Count);
    }
}

[Route("/first-press-after-a-title")]
public sealed partial class FirstPressTitleLayout(RouteState route) : Component
{
    private bool _open;

    protected override Component? Render() =>
        Div[
            Button.Id("go").Key("go").OnClick(() => _open = !_open)["Go"],
            P.Id("asked")["Asked:" + _open + " " + route.Title],
            Main[Outlet]
        ];
}

[Route("child")]
[ParentRoute(typeof(FirstPressTitleLayout))]
public sealed partial class FirstPressTitleChildPage : Component
{
    protected override string? PageTitle => "Kid";

    protected override Component? Render() => P["child"];
}
