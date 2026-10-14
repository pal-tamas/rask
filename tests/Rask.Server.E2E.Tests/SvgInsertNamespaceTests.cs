using Rask.Core;
using Rask.Server.E2E.Tests.Infrastructure;
using Rask.Site.E2E.Tests.Infrastructure;
using static Microsoft.Playwright.Assertions;

#pragma warning disable RASK019 // a small test page; its <head> is not what is under test

namespace Rask.Server.E2E.Tests;

/// <summary>
///     An <c>&lt;svg&gt;</c> that gains a child after a state change, patched in a real browser.
/// </summary>
/// <remarks>
///     #1242: the client parsed every inserted subtree in a <c>&lt;template&gt;</c>, which parses HTML, so a
///     <c>&lt;circle&gt;</c> inserted on its own under an <c>&lt;svg&gt;</c> became an <c>HTMLUnknownElement</c> and
///     never drew. Only a browser's own parser shows which namespace the node landed in, so this is where it is pinned:
///     once through the trusted tail append, once through a keyed insert ahead of the existing children.
/// </remarks>
public sealed class SvgInsertNamespaceTests(PlaywrightFixture playwright) : IClassFixture<PlaywrightFixture>
{
    private const string CirclesInSvgNamespace =
        "svg => [...svg.children].map(c => c.namespaceURI === 'http://www.w3.org/2000/svg' && c instanceof SVGCircleElement)";

    [Fact]
    public async Task A_circle_appended_to_an_svg_after_a_click_lands_in_the_svg_namespace_and_draws()
    {
        await using var session = await HookSession.OpenAsync<SvgGrowsPage>(playwright);
        var page = session.Page;
        var chart = page.Locator("#tail");
        await Expect(chart.Locator("circle")).ToHaveCountAsync(2);

        await page.ClickAsync("#add");

        await Expect(chart.Locator("circle")).ToHaveCountAsync(3);
        Assert.Equal(new[] { true, true, true }, await chart.EvaluateAsync<bool[]>(CirclesInSvgNamespace));
        var width = await chart.EvaluateAsync<double>("svg => svg.lastElementChild.getBBox().width");
        Assert.True(width > 0, $"the appended circle must draw; its box was {width} wide");
    }

    [Fact]
    public async Task A_keyed_circle_inserted_ahead_of_its_siblings_in_an_svg_lands_in_the_svg_namespace()
    {
        await using var session = await HookSession.OpenAsync<SvgGrowsPage>(playwright);
        var page = session.Page;
        var chart = page.Locator("#keyed");
        await Expect(chart.Locator("circle")).ToHaveCountAsync(2);

        await page.ClickAsync("#add");

        await Expect(chart.Locator("circle")).ToHaveCountAsync(3);
        Assert.Equal(new[] { true, true, true }, await chart.EvaluateAsync<bool[]>(CirclesInSvgNamespace));
        Assert.Equal("c3", await chart.EvaluateAsync<string>("svg => svg.firstElementChild.id"));
        Assert.Empty(session.PageErrors);
    }
}

/// <summary>Two charts that gain a circle on the click: one appended after two written-out circles, one keyed at the front.</summary>
public sealed partial class SvgGrowsPage : Component
{
    private int _count = 2;

    protected override Component? HeadAssets => Markup.Title["svg insert namespace"];

    protected override string? HtmlLang => "en";

    protected override Component? Render() =>
        Div[
            Button.Id("add").OnClick(() => _count++)["add"],
            Svg.Id("tail").Width("200").Height("40").ViewBox("0 0 200 40")[
                Circle.Cx("20").Cy("20").R("8"),
                Circle.Cx("40").Cy("20").R("8"),
                _count > 2 ? Circle.Cx("60").Cy("20").R("8") : null],
            Svg.Id("keyed").Width("200").Height("40").ViewBox("0 0 200 40")[
                Enumerable.Range(1, _count).Reverse()
                    .Select(i => Circle.Key($"c{i}").Id($"c{i}").Cx($"{i * 20}").Cy("20").R("8"))]
        ];
}
