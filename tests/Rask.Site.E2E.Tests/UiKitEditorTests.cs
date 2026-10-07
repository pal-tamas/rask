using Microsoft.Playwright;
using Rask.Site.E2E.Tests.Infrastructure;
using static Microsoft.Playwright.Assertions;

namespace Rask.Site.E2E.Tests;

/// <summary>
///     The kit's rich text editor, in a browser: the engine arriving only when an editor is on the page, the
///     value crossing to C# and back, and a re-render leaving what was typed alone.
/// </summary>
[Collection(WasmExampleCollection.Name)]
public sealed class UiKitEditorTests(WasmExampleAppFixture app, PlaywrightFixture pw)
    : SharedSmokeTests(pw)
{
    private const string Engine = "rask-ui-editor.js";

    protected override string BaseUrl => app.BaseUrl;
    protected override string FixtureName => "Wasm";
    protected override string ServerLog => app.ServerLog;

    private ILocator Editors => Page.Locator("[data-testid='ui-editor'] [data-ui-editor]");

    private ILocator Value => Page.Locator("[data-testid='ui-editor-value']");

    [Fact]
    public Task The_engine_is_fetched_once_and_only_by_a_page_that_draws_an_editor() => RunAsync(async () =>
    {
        var fetched = new List<string>();
        Page.Request += (_, request) =>
        {
            if (request.Url.Contains(Engine, StringComparison.Ordinal))
            {
                fetched.Add(request.Url);
            }
        };

        await Page.GotoAsync(Docs);
        await Expect(Page.Locator(".side-nav a.side-nav-link[aria-current='page']").First).ToBeVisibleAsync(
            new LocatorAssertionsToBeVisibleOptions { Timeout = 30_000 });
        var before = fetched.Count;
        await OpenAsync(alreadyOnDocs: true);

        Assert.Equal(0, before);
        Assert.Single(fetched);
        Assert.Contains("?v=", fetched[0], StringComparison.Ordinal);
        await Expect(Editors).ToHaveCountAsync(6);
    });

    [Fact]
    public Task Typing_and_the_toolbar_change_the_bound_value_which_the_page_shows_as_text() => RunAsync(async () =>
    {
        await OpenAsync();
        var editor = Editors.First;
        var area = editor.Locator("[data-slot='content']");
        await Expect(Value).ToContainTextAsync("<h3>What's changed</h3>");

        await area.ClickAsync();
        await Page.Keyboard.PressAsync("ControlOrMeta+a");
        await Page.Keyboard.TypeAsync("Hello");
        await Expect(Value).ToHaveTextAsync("<p>Hello</p>");
        await Page.Keyboard.PressAsync("ControlOrMeta+a");
        await editor.Locator("[data-editor='bold']").ClickAsync();

        await Expect(Value).ToHaveTextAsync("<p><strong>Hello</strong></p>");
        await Expect(editor.Locator("[data-editor='bold']")).ToHaveAttributeAsync("aria-pressed", "true");
        await Expect(area).ToBeFocusedAsync();
        // The reader's HTML is shown, never run: the <pre> holds text and no element of theirs.
        await Expect(Value.Locator("strong")).ToHaveCountAsync(0);
        await Page.Keyboard.PressAsync("ControlOrMeta+a");
        await Page.Keyboard.PressAsync("Backspace");
        await Expect(Value).ToHaveTextAsync("(empty)");
    });

    [Fact]
    public Task Markdown_rules_the_heading_list_and_the_link_panel_shape_the_document() => RunAsync(async () =>
    {
        await OpenAsync();
        var editor = Editors.First;
        var area = editor.Locator("[data-slot='content']");

        await area.ClickAsync();
        await Page.Keyboard.PressAsync("ControlOrMeta+a");
        await Page.Keyboard.TypeAsync("# Title");
        await Expect(Value).ToHaveTextAsync("<h1>Title</h1>");
        await editor.Locator("[data-editor='heading'] button").ClickAsync();
        await Expect(editor.Locator("[data-editor='heading'] [role='listbox']")).ToBeVisibleAsync();
        await editor.Locator("[data-editor='heading'] [role='option'][data-value='heading2']").ClickAsync();
        await Expect(Value).ToHaveTextAsync("<h2>Title</h2>");
        await Page.Keyboard.PressAsync("ControlOrMeta+a");
        await editor.Locator("[data-editor='link'] button:not([data-editor])").ClickAsync();
        await Expect(editor.Locator("[data-editor='link:url']")).ToBeFocusedAsync();
        await Page.Keyboard.TypeAsync("https://rask.sh");
        await Page.Keyboard.PressAsync("Enter");

        await Expect(Value).ToHaveTextAsync(
            "<h2><a target=\"_blank\" rel=\"noopener noreferrer nofollow\" href=\"https://rask.sh\">Title</a></h2>");
        await Expect(editor.Locator("[data-editor='link'] > [popover]")).ToBeHiddenAsync();
        await editor.Locator("[data-editor='link'] button:not([data-editor])").ClickAsync();
        await editor.Locator("[data-editor='link:unlink']").ClickAsync();
        await Expect(Value).ToHaveTextAsync("<h2>Title</h2>");
    });

    [Fact]
    public Task A_button_of_the_apps_own_runs_csharp_and_the_render_it_causes_leaves_the_typing_alone() => RunAsync(async () =>
    {
        await OpenAsync();
        var composed = Page.Locator("[data-example='Customization'] [data-ui-editor]");
        var area = composed.Locator("[data-slot='content']");

        await area.ClickAsync();
        await Page.Keyboard.TypeAsync("Still here");
        await composed.Locator("[role='toolbar'] button").Last.ClickAsync();

        await Expect(Page.Locator("[data-testid='ui-editor-copied']")).ToContainTextAsync("Copied ");
        await Expect(area).ToHaveTextAsync("Still here");
        await Expect(area).ToHaveAttributeAsync("contenteditable", "true");
        // The placeholder is the sheet's, drawn from the paragraph the engine marks: an untouched editor shows it.
        var untouched = Page.Locator("[data-example='Configuring items'] [data-slot='content'] p");
        Assert.Equal(
            "\"Write something...\"",
            await untouched.EvaluateAsync<string>("p => getComputedStyle(p, '::before').content"));
    });

    [Fact]
    public Task A_disabled_editor_cannot_be_typed_into_and_its_toolbar_is_off() => RunAsync(async () =>
    {
        await OpenAsync();
        var disabled = Page.Locator("[data-example='Disabled'] [data-ui-editor]");

        var area = disabled.Locator("[data-slot='content']");

        await Expect(area).ToHaveAttributeAsync("contenteditable", "false");
        await Expect(area).ToHaveTextAsync("This one is read-only.");
        await Expect(disabled.Locator("[data-editor='bold']")).ToBeDisabledAsync();
        await Expect(disabled.Locator("[data-editor='heading'] button")).ToBeDisabledAsync();
    });

    private async Task OpenAsync(bool alreadyOnDocs = false)
    {
        if (!alreadyOnDocs)
        {
            await Page.GotoAsync(Docs);
            await Expect(Page.Locator(".side-nav a.side-nav-link[aria-current='page']").First).ToBeVisibleAsync(
                new LocatorAssertionsToBeVisibleOptions { Timeout = 30_000 });
        }

        await ClickSidebar("Data input");
        await Expect(Page.Locator("main h1")).ToContainTextAsync("Data input",
            new LocatorAssertionsToContainTextOptions { Timeout = 15_000 });

        // Mounted: the engine has replaced the first paint with its editable surface.
        await Expect(Editors.First.Locator("[data-slot='content'][contenteditable]")).ToBeVisibleAsync(
            new LocatorAssertionsToBeVisibleOptions { Timeout = 30_000 });
        await Expect(Page.Locator("[data-testid='ui-editor'] [data-slot='content'][contenteditable]")).ToHaveCountAsync(
            6, new LocatorAssertionsToHaveCountOptions { Timeout = 30_000 });
    }
}
