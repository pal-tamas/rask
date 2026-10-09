using Rask.Core;
using Rask.Server.E2E.Tests.Infrastructure;
using Rask.Site.E2E.Tests.Infrastructure;
using static Microsoft.Playwright.Assertions;

#pragma warning disable RASK019 // a small test page; its <head> is not what is under test

namespace Rask.Server.E2E.Tests;

/// <summary>
///     A component whose whole render is bare text, in a breadcrumb of the layout, patched in a real browser.
/// </summary>
/// <remarks>
///     #1238: the crumb was written <c>Text[heading.Text]</c> and stayed <c>&lt;div&gt;&lt;/div&gt;</c> for ever,
///     while <c>Span[heading.Text]</c> in its place was patched. The unit suites pin the frames and the ops; what
///     only a browser proves is that a text node with no element of its own is inserted into, changed in and
///     removed from somebody else's element by the real <c>rask.js</c>.
/// </remarks>
public sealed class BareTextRootTests(PlaywrightFixture playwright) : IClassFixture<PlaywrightFixture>
{
    private const string Step = "[data-ui-breadcrumbs-item] > div";

    [Fact]
    public async Task A_crumb_that_is_bare_text_shows_what_the_page_named_as_it_mounted()
    {
        await using var session = await HookSession.OpenAsync<HeadingLayoutPage>(playwright);
        var page = session.Page;

        var served = await page.EvaluateAsync<string>("async () => (await (await fetch(location.href)).text())");

        await Expect(page.Locator("#bracket " + Step)).ToHaveTextAsync("Orders");
        await Expect(page.Locator("#value " + Step)).ToHaveTextAsync("Orders");
        await Expect(page.Locator("#layout-heading")).ToHaveTextAsync("Orders");
        // Not one render behind: the response itself carries it, before any socket exists.
        Assert.Contains(">Orders</div>", served);
        Assert.Contains("<h1 id=\"layout-heading\">Orders</h1>", served);
    }

    [Fact]
    public async Task A_crumb_that_is_bare_text_follows_its_heading_through_change_empty_and_back()
    {
        await using var session = await HookSession.OpenAsync<HeadingLayoutPage>(playwright);
        var page = session.Page;
        var bracket = page.Locator("#bracket " + Step);
        var value = page.Locator("#value " + Step);
        await Expect(bracket).ToHaveTextAsync("Orders");

        await page.ClickAsync("#rename");
        await Expect(bracket).ToHaveTextAsync("Invoices");
        await Expect(value).ToHaveTextAsync("Invoices");
        await page.ClickAsync("#clear");
        await Expect(bracket).ToHaveTextAsync("");
        var emptied = await bracket.EvaluateAsync<int>("el => el.childNodes.length");
        await page.ClickAsync("#rename");

        await Expect(bracket).ToHaveTextAsync("Invoices");
        await Expect(value).ToHaveTextAsync("Invoices");
        await Expect(page.Locator("#layout-heading")).ToHaveTextAsync("Invoices");
        Assert.Equal(0, emptied);
        Assert.Equal(1, await bracket.EvaluateAsync<int>("el => el.childNodes.length"));
    }
}

/// <summary>What the page is called: set by the page, shown by the layout above it.</summary>
public sealed class PageHeading
{
    public string Text { get; private set; } = string.Empty;

    public event EventHandler? Changed;

    public void Set(string text)
    {
        Text = text;
        Changed?.Invoke(this, EventArgs.Empty);
    }
}

/// <summary>The reported leaf: it follows the heading and renders it as bare text, handed over as children.</summary>
public sealed partial class BracketHeadingText : Component
{
    public required PageHeading Heading { get; set; }

    protected override Task OnMount()
    {
        Heading.Changed += StateHasChanged;
        return Task.CompletedTask;
    }

    protected override Task OnUnmount()
    {
        Heading.Changed -= StateHasChanged;
        return Task.CompletedTask;
    }

    protected override Component? Render() => Text[Heading.Text];
}

/// <summary>The same leaf, with the words given as the value.</summary>
public sealed partial class ValueHeadingText : Component
{
    public required PageHeading Heading { get; set; }

    protected override Task OnMount()
    {
        Heading.Changed += StateHasChanged;
        return Task.CompletedTask;
    }

    protected override Task OnUnmount()
    {
        Heading.Changed -= StateHasChanged;
        return Task.CompletedTask;
    }

    protected override Component? Render() => Text.Value(Heading.Text);
}

/// <summary>A page that names itself as it mounts, and again when asked.</summary>
public sealed partial class HeadingPage : Component
{
    public required PageHeading Heading { get; set; }

    protected override Task OnMount()
    {
        Heading.Set("Orders");
        return Task.CompletedTask;
    }

    protected override Component? Render() =>
    [
        Button.Id("rename").OnClick(() => Heading.Set("Invoices"))["rename"],
        Button.Id("clear").OnClick(() => Heading.Set(string.Empty))["clear"]
    ];
}

/// <summary>Mounts the page one render below the layout, so it mounts after the crumbs above it were walked.</summary>
public sealed partial class HeadingOutlet : Component
{
    public required PageHeading Heading { get; set; }

    protected override Component? Render() => Main[HeadingPage.Heading(Heading)];
}

/// <summary>The layout: two crumbs that are bare text, and a heading of its own it re-renders when the page asks.</summary>
public sealed partial class HeadingLayout : Component
{
    private readonly PageHeading _heading = new();

    protected override Task OnMount()
    {
        _heading.Changed += StateHasChanged;
        return Task.CompletedTask;
    }

    protected override Task OnUnmount()
    {
        _heading.Changed -= StateHasChanged;
        return Task.CompletedTask;
    }

    protected override Component? Render() =>
    [
        Nav.Id("bracket")[Ui.Breadcrumbs[Ui.BreadcrumbsItem.Key("bracket")[BracketHeadingText.Heading(_heading)]]],
        Nav.Id("value")[Ui.Breadcrumbs[Ui.BreadcrumbsItem.Key("value")[ValueHeadingText.Heading(_heading)]]],
        H1.Id("layout-heading")[_heading.Text],
        HeadingOutlet.Heading(_heading)
    ];
}

public sealed partial class HeadingLayoutPage : Component
{
    protected override Component? HeadAssets => Markup.Title["bare text root"];

    protected override string? HtmlLang => "en";

    protected override Component? Render() => Div[HeadingLayout];
}
