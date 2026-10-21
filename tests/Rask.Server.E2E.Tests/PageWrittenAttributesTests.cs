using Microsoft.Playwright;
using Rask.Core;
using Rask.Core.Components;
using Rask.Core.Live;
using Rask.Server.E2E.Tests.Infrastructure;
using Rask.Site.E2E.Tests.Infrastructure;
using static Microsoft.Playwright.Assertions;

#pragma warning disable RASK019 // a small test page; its <head> is not what is under test

namespace Rask.Server.E2E.Tests;

/// <summary>
///     A patch never removes an attribute the server never wrote (TA): a canvas whose script set its width and height
///     keeps them when a form error appears around it — answered with a diff that morphs the canvas's parent, and
///     answered with the whole page. What the server DID write and then drops still goes.
/// </summary>
public sealed class PageWrittenAttributesTests(PlaywrightFixture playwright) : IClassFixture<PlaywrightFixture>
{
    [Theory]
    [InlineData(LiveDiffMode.Auto)]
    [InlineData(LiveDiffMode.DisabledFull)]
    public async Task A_canvas_keeps_the_size_its_script_gave_it_when_an_error_appears_and_goes(LiveDiffMode mode)
    {
        await using var session = await HookSession.OpenAsync<GroundPage>(playwright, live: o => o.DiffMode = mode);
        var page = session.Page;
        var canvas = page.Locator("#ground");
        await Expect(canvas).ToHaveAttributeAsync("title", "ground");
        await canvas.EvaluateAsync("c => { c.width = 640; c.height = 320; c.setAttribute('data-drawn', 'yes'); }");

        await page.ClickAsync("#fail");
        await Expect(page.Locator("#error")).ToHaveTextAsync("A mező kötelező.");
        await page.ClickAsync("#fix");
        await Expect(page.Locator("#error")).ToHaveCountAsync(0);

        await Expect(canvas).ToHaveAttributeAsync("width", "640");
        await Expect(canvas).ToHaveAttributeAsync("height", "320");
        await Expect(canvas).ToHaveAttributeAsync("data-drawn", "yes");
        await Expect(canvas).ToHaveAttributeAsync("aria-hidden", "true");
        await Expect(canvas).Not.ToHaveAttributeAsync("title", "ground");
        Assert.Null(await canvas.GetAttributeAsync("data-rask-tip"));
        Assert.Empty(session.PageErrors);
    }

    [Theory]
    [InlineData(LiveDiffMode.Auto)]
    [InlineData(LiveDiffMode.DisabledFull)]
    public async Task An_attribute_the_page_added_goes_once_the_server_has_written_it_and_then_drops_it(LiveDiffMode mode)
    {
        await using var session = await HookSession.OpenAsync<GroundPage>(playwright, live: o => o.DiffMode = mode);
        var page = session.Page;
        var canvas = page.Locator("#ground");
        await Expect(canvas).ToHaveAttributeAsync("title", "ground");
        await canvas.EvaluateAsync("c => c.setAttribute('data-mode', 'mine')");

        await page.ClickAsync("#mode");
        await Expect(canvas).ToHaveAttributeAsync("data-mode", "server");
        await page.ClickAsync("#mode");
        await Expect(page.Locator("#said")).ToHaveTextAsync("modes: 2");

        Assert.Null(await canvas.GetAttributeAsync("data-mode"));
        Assert.Empty(session.PageErrors);
    }
}

/// <summary>
///     A canvas drawn by a script, under an error that takes the place of a hint: one child in the place of another,
///     which the differ answers by morphing the parent the canvas sits in.
/// </summary>
public sealed partial class GroundPage : Component
{
    private readonly ElementRef<HTMLCanvasElement> _ground = new();
    private bool _error;
    private bool _wasWrong;
    private int _modes;

    protected override Component? HeadAssets => Markup.Title["page-written attributes"];

    protected override string? HtmlLang => "hu";

    protected override Component? Render() =>
        Div[
            Button.Id("fail").OnClick(() => _error = _wasWrong = true)["fail"],
            Button.Id("fix").OnClick(() => _error = false)["fix"],
            Button.Id("mode").OnClick(() => _modes++)["mode"],
            P.Id("said")[$"modes: {_modes}"],
            Div.Class("field")[
                _error ? P.Id("error").Class("text-red-600")["A mező kötelező."] : Span.Class("hint")["Adja meg a mezőt."],
                Ground(),
                _error ? Strong["!"] : Em["?"]
            ]
        ];

    // What the server writes on the canvas: a title and a hook attribute until the first error, a mode once.
    private Component Ground()
    {
        var data = new Dictionary<string, string?>(StringComparer.Ordinal);
        if (_modes == 1)
        {
            data["mode"] = "server";
        }

        if (!_wasWrong)
        {
            data["rask-tip"] = "ground";
        }

        var canvas = Canvas.Id("ground").Ref(_ground).Class("absolute inset-0").AriaHidden(true).Data(data);
        return _wasWrong ? canvas : canvas.Title("ground");
    }
}
