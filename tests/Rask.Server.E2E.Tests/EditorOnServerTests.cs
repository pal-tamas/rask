using Microsoft.Playwright;
using Rask.Core;
using Rask.Core.Components;
using Rask.Server.E2E.Tests.Infrastructure;
using Rask.Site.E2E.Tests.Infrastructure;
using static Microsoft.Playwright.Assertions;

#pragma warning disable RASK019 // a small test page; its <head> is not what is under test

namespace Rask.Server.E2E.Tests;

/// <summary>
///     <c>Ui.Editor</c> on a server-rendered page: its engine is a static file the app's build wrote, its scoped
///     script arrives in Rask's bundle, and its value crosses the socket in both directions.
/// </summary>
/// <remarks>
///     The site proves the editor in a browser-WASM app. What only a Server app has is the socket between the
///     editor's callback and the model, and a host that serves the engine from its own <c>wwwroot</c>, where
///     the kit's build wrote it.
/// </remarks>
public sealed class EditorOnServerTests(PlaywrightFixture playwright) : IClassFixture<PlaywrightFixture>
{
    private const string Engine = "/js/rask-ui-editor.js";

    [Fact]
    public async Task What_is_typed_reaches_the_bound_model_and_what_the_server_sets_reaches_the_editor()
    {
        await using var host = await LiveServerHost.StartAsync<EditorPage>(blockWebSockets: false, staticFiles: true);
        await using var context = await playwright.Browser.NewContextAsync();
        var page = await context.NewPageAsync();
        var engine = new List<IResponse>();
        page.Response += (_, response) =>
        {
            if (response.Url.Contains(Engine, StringComparison.Ordinal))
            {
                engine.Add(response);
            }
        };
        await page.GotoAsync(host.BaseUrl + "/");
        var area = page.Locator("[data-ui-editor] [data-slot='content']");
        await Expect(area).ToHaveAttributeAsync("contenteditable", "true", new() { Timeout = 15_000 });

        await area.ClickAsync();
        await page.Keyboard.PressAsync("ControlOrMeta+a");
        await page.Keyboard.TypeAsync("Hello");
        await Expect(page.Locator("#value")).ToHaveTextAsync("<p>Hello</p>", new() { Timeout = 15_000 });
        await page.Locator("#set").ClickAsync();

        await Expect(page.Locator("#value")).ToHaveTextAsync("<p>From <strong>the server</strong></p>", new() { Timeout = 15_000 });
        await Expect(area).ToHaveTextAsync("From the server");
        await Expect(area.Locator("strong")).ToHaveTextAsync("the server");
        await Expect(area).ToHaveAttributeAsync("contenteditable", "true");
        var fetched = Assert.Single(engine);
        Assert.Equal(200, fetched.Status);
        Assert.Contains(Engine + "?v=", fetched.Url, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_re_render_the_page_causes_leaves_what_is_being_typed_alone()
    {
        await using var host = await LiveServerHost.StartAsync<EditorPage>(blockWebSockets: false, staticFiles: true);
        await using var context = await playwright.Browser.NewContextAsync();
        var page = await context.NewPageAsync();
        await page.GotoAsync(host.BaseUrl + "/");
        var area = page.Locator("[data-ui-editor] [data-slot='content']");
        await Expect(area).ToHaveAttributeAsync("contenteditable", "true", new() { Timeout = 15_000 });

        await area.ClickAsync();
        await page.Keyboard.PressAsync("ControlOrMeta+a");
        await page.Keyboard.TypeAsync("Still here");
        await page.Locator("#tick").ClickAsync();

        await Expect(page.Locator("#ticks")).ToHaveTextAsync("ticks=1", new() { Timeout = 15_000 });
        await Expect(area).ToHaveTextAsync("Still here");
        await Expect(page.Locator("#value")).ToHaveTextAsync("<p>Still here</p>");
    }

    [Fact]
    public async Task Without_the_engine_file_the_editor_keeps_its_first_paint_and_the_page_stays_live()
    {
        await using var host = await LiveServerHost.StartAsync<EditorPage>(blockWebSockets: false);
        await using var context = await playwright.Browser.NewContextAsync();
        var page = await context.NewPageAsync();
        var errors = new System.Collections.Concurrent.ConcurrentQueue<string>();
        page.Console += (_, message) =>
        {
            if (message.Type == "error")
            {
                errors.Enqueue(message.Text);
            }
        };
        await page.GotoAsync(host.BaseUrl + "/");
        var area = page.Locator("[data-ui-editor] [data-slot='content']");
        await Expect(area).ToHaveTextAsync("Start", new() { Timeout = 15_000 });

        await page.Locator("#tick").ClickAsync();

        await Expect(page.Locator("#ticks")).ToHaveTextAsync("ticks=1", new() { Timeout = 15_000 });
        Assert.Null(await area.GetAttributeAsync("contenteditable"));
        // The one place an app learns why: the console names the file its host is not serving. The message is
        // the browser's to deliver, after the import that failed: on a loaded machine the page is live first.
        for (var waited = 0; errors.IsEmpty && waited < 15_000; waited += 50)
        {
            await Task.Delay(50, TestContext.Current.CancellationToken);
        }

        Assert.Contains(errors.ToArray(), error => error.Contains("wwwroot/js/rask-ui-editor.js", StringComparison.Ordinal));
    }
}

public sealed partial class EditorPage : Component
{
    private readonly EditorPost _post = new();
    private int _ticks;

    protected override Component? HeadAssets => Markup.Title["editor"];
    protected override string? HtmlLang => "en";

    protected override Component? Render() =>
    [
        Ui.Editor.Bind(() => _post.Body).Label("Notes"),
        Pre.Id("value")[string.IsNullOrEmpty(_post.Body) ? "(empty)" : _post.Body],
        Button.Id("set").OnClick(() => _post.Body = "<p>From <strong>the server</strong></p>")["set"],
        Button.Id("tick").OnClick(() => _ticks++)["tick"],
        P.Id("ticks")[$"ticks={_ticks}"]
    ];
}

/// <summary>What the page binds its editor to: a property, as a form model has.</summary>
public sealed class EditorPost
{
    public string? Body { get; set; } = "<p>Start</p>";
}
