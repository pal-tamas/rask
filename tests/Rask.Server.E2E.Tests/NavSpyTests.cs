using Microsoft.Playwright;
using Rask.Core;
using Rask.Server.E2E.Tests.Infrastructure;
using Rask.Site.E2E.Tests.Infrastructure;
using static Microsoft.Playwright.Assertions;

#pragma warning disable RASK019 // small test pages; their <head> is not what is under test

namespace Rask.Server.E2E.Tests;

/// <summary>
///     A <c>NavLink</c> to a section of its own page (<c>/#contact</c>) on a live Server page: the server cannot see
///     the fragment, so the runtime lights the link while its section is the one being read (rask-nav-spy.ts) —
///     its active class and <c>aria-current</c> — and none above the first section.
/// </summary>
public sealed class NavSpyTests(PlaywrightFixture playwright) : IClassFixture<PlaywrightFixture>
{
    [Fact]
    public async Task Above_the_first_section_no_section_link_is_lit()
    {
        await using var session = await HookSession.OpenAsync<NavSpyPage>(playwright);
        var page = session.Page;

        await page.WaitForTimeoutAsync(300);

        await Expect(page.Locator("a[aria-current=true]")).ToHaveCountAsync(0);
        await Expect(page.Locator("#to-about")).Not.ToHaveClassAsync(new System.Text.RegularExpressions.Regex("lit"));
    }

    [Fact]
    public async Task The_link_of_the_section_scrolled_to_is_lit_and_the_others_are_not()
    {
        await using var session = await HookSession.OpenAsync<NavSpyPage>(playwright);
        var page = session.Page;

        await page.EvaluateAsync("() => document.getElementById('services').scrollIntoView()");

        await Expect(page.Locator("#to-services")).ToHaveAttributeAsync("aria-current", "true");
        await Expect(page.Locator("#to-services")).ToHaveClassAsync("link lit");
        await Expect(page.Locator("#to-about")).ToHaveClassAsync("link");
        await Expect(page.Locator("#to-about")).Not.ToHaveAttributeAsync("aria-current", "true");
    }

    [Fact]
    public async Task At_the_bottom_a_short_last_section_is_lit()
    {
        await using var session = await HookSession.OpenAsync<NavSpyPage>(playwright);
        var page = session.Page;

        await page.EvaluateAsync("() => window.scrollTo(0, document.documentElement.scrollHeight)");

        await Expect(page.Locator("#to-contact")).ToHaveAttributeAsync("aria-current", "true");
        await Expect(page.Locator("#to-services")).Not.ToHaveAttributeAsync("aria-current", "true");
    }

    [Fact]
    public async Task Clicking_a_section_link_lights_it()
    {
        await using var session = await HookSession.OpenAsync<NavSpyPage>(playwright);
        var page = session.Page;

        await page.ClickAsync("#to-about");

        await Expect(page.Locator("#to-about")).ToHaveAttributeAsync("aria-current", "true");
    }
}

/// <summary>A fixed header of section links over a hero and three sections, the last one short.</summary>
public sealed partial class NavSpyPage : Component
{
    protected override Component? HeadAssets => Markup.Title["nav spy"];

    protected override string? HtmlLang => "en";

    protected override Component? Render() =>
    [
        Header.Style("position:fixed;top:0;left:0;right:0;background:#fff")[
            NavLink.Id("to-about").Class("link").ActiveClass("lit").Href("/#about")["About"],
            NavLink.Id("to-services").Class("link").ActiveClass("lit").Href("/#services")["Services"],
            NavLink.Id("to-contact").Class("link").ActiveClass("lit").Href("/#contact")["Contact"]
        ],
        Section.Id("hero").Style("height:1200px")["hero"],
        Section.Id("about").Style("height:1200px")["about"],
        Section.Id("services").Style("height:1200px")["services"],
        Section.Id("contact").Style("height:200px")["contact"]
    ];
}
