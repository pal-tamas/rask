using System.ComponentModel.DataAnnotations;
using Microsoft.Playwright;
using Rask.Core;
using Rask.Core.Routing;
using Rask.Server.E2E.Tests.Infrastructure;
using Rask.Site.E2E.Tests.Infrastructure;
using static Microsoft.Playwright.Assertions;

#pragma warning disable RASK019 // a small test page; its <head> is not what is under test

namespace Rask.Server.E2E.Tests;

/// <summary>
///     <c>Form.Model(m).ConfirmLeave("…")</c> in a real browser: a form the reader has typed into asks before
///     any way out of the page, staying keeps the page exactly as it was, and a save ends the guard.
/// </summary>
/// <remarks>
///     Every test counts the dialogs it was shown, because the two failures look the same from the page: a
///     guard that never asked and a guard that asked and was answered. The browser's own leave prompt
///     (<c>beforeunload</c>) is driven through a reload, which is the one exit a test can both start and refuse.
/// </remarks>
public sealed class ConfirmLeaveHookTests(PlaywrightFixture playwright) : IClassFixture<PlaywrightFixture>
{
    private const string Message = "Leave without saving?";

    [Fact]
    public async Task A_form_nobody_typed_into_lets_a_nav_link_through_without_asking()
    {
        await using var session = await HookSession.OpenAsync<LeaveGuardPage>(playwright);
        var asked = Answer(session.Page, stay: true);

        await session.HooksLoadedAsync();
        await session.Page.ClickAsync("#to-list");

        await Expect(session.Page.Locator("#path")).ToHaveTextAsync("path=/list");
        Assert.Empty(asked);
    }

    [Fact]
    public async Task Staying_after_a_press_on_a_nav_link_keeps_the_page_and_what_was_typed_and_sends_nothing()
    {
        await using var session = await HookSession.OpenAsync<LeaveGuardPage>(playwright);
        var page = session.Page;
        var asked = Answer(page, stay: true);

        await page.FillAsync("#title", "Minutes");
        await page.ClickAsync("#to-list");
        await page.ClickAsync("#count");

        await Expect(page.Locator("#counted")).ToHaveTextAsync("count=1");
        await Expect(page.Locator("#path")).ToHaveTextAsync("path=/");
        await Expect(page.Locator("#title")).ToHaveValueAsync("Minutes");
        Assert.Equal(["confirm:" + Message], asked);
        Assert.EndsWith("/", page.Url, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Leaving_after_a_press_on_a_nav_link_navigates_and_the_form_that_left_guards_nothing()
    {
        await using var session = await HookSession.OpenAsync<LeaveGuardPage>(playwright);
        var page = session.Page;
        var asked = Answer(page, stay: false);

        await page.FillAsync("#title", "Minutes");
        await page.ClickAsync("#to-list");
        await Expect(page.Locator("#path")).ToHaveTextAsync("path=/list");
        await page.ClickAsync("#to-other");

        await Expect(page.Locator("#path")).ToHaveTextAsync("path=/other");
        Assert.Equal(["confirm:" + Message], asked);
    }

    [Fact]
    public async Task A_navigation_from_front_end_code_asks_like_a_link_does()
    {
        await using var session = await HookSession.OpenAsync<LeaveGuardPage>(playwright);
        var page = session.Page;
        var asked = Answer(page, stay: true);

        await page.FillAsync("#title", "Minutes");
        await page.EvaluateAsync("() => window.__raskHost.navigate('/list')");
        await page.ClickAsync("#count");

        await Expect(page.Locator("#counted")).ToHaveTextAsync("count=1");
        await Expect(page.Locator("#path")).ToHaveTextAsync("path=/");
        Assert.Equal(["confirm:" + Message], asked);
    }

    [Fact]
    public async Task A_save_that_goes_elsewhere_from_its_handler_is_never_asked_about()
    {
        await using var session = await HookSession.OpenAsync<LeaveGuardPage>(playwright);
        var page = session.Page;
        var asked = Answer(page, stay: true);

        await page.FillAsync("#title", "go");
        await page.ClickAsync("#save");

        await Expect(page.Locator("#path")).ToHaveTextAsync("path=/list");
        await Expect(page.Locator("#saved")).ToHaveTextAsync("saved=go");
        Assert.Empty(asked);
    }

    [Fact]
    public async Task A_save_that_stays_on_the_page_ends_the_guard_until_the_next_edit()
    {
        await using var session = await HookSession.OpenAsync<LeaveGuardPage>(playwright);
        var page = session.Page;
        var asked = Answer(page, stay: true);

        await page.FillAsync("#title", "Minutes");
        await page.ClickAsync("#save");
        await Expect(page.Locator("#saved")).ToHaveTextAsync("saved=Minutes");
        var unsavedAfterTheSave = await UnsavedAsync(page);
        await page.FillAsync("#title", "Minutes, again");
        await page.ClickAsync("#to-list");

        Assert.False(unsavedAfterTheSave);
        await Expect(page.Locator("#path")).ToHaveTextAsync("path=/");
        Assert.Equal(["confirm:" + Message], asked);
    }

    [Fact]
    public async Task A_submit_validation_refuses_leaves_the_form_guarded()
    {
        await using var session = await HookSession.OpenAsync<LeaveGuardPage>(playwright);
        var page = session.Page;
        var asked = Answer(page, stay: true);

        await page.FillAsync("#body", "No title yet");
        await page.ClickAsync("#save");
        await Expect(page.Locator("#refused")).ToHaveTextAsync("refused=1");
        await page.ClickAsync("#to-list");
        await page.ClickAsync("#count");

        await Expect(page.Locator("#counted")).ToHaveTextAsync("count=1");
        await Expect(page.Locator("#path")).ToHaveTextAsync("path=/");
        Assert.Equal(["confirm:" + Message], asked);
    }

    [Fact]
    public async Task Staying_after_Back_puts_the_address_and_the_history_where_they_were()
    {
        await using var session = await HookSession.OpenAsync<LeaveGuardPage>(playwright);
        var page = session.Page;
        await page.ClickAsync("#to-list");
        await Expect(page.Locator("#path")).ToHaveTextAsync("path=/list");
        await page.ClickAsync("#to-form");
        await Expect(page.Locator("#title")).ToBeVisibleAsync();
        var stay = true;
        var asked = Answer(page, () => stay);

        await page.FillAsync("#title", "Minutes");
        await page.EvaluateAsync("() => history.back()");
        await AskedAsync(asked, times: 1);
        await page.ClickAsync("#count");
        await Expect(page.Locator("#counted")).ToHaveTextAsync("count=1");
        var addressAfterStaying = new Uri(page.Url).AbsolutePath;
        var pathAfterStaying = await page.TextContentAsync("#path");
        var typedAfterStaying = await page.InputValueAsync("#title");
        stay = false;
        await page.EvaluateAsync("() => history.back()");

        // One entry back from where the reader stayed is the list: the refused Back moved nothing.
        await Expect(page.Locator("#path")).ToHaveTextAsync("path=/list");
        Assert.Equal("/", addressAfterStaying);
        Assert.Equal("path=/", pathAfterStaying);
        Assert.Equal("Minutes", typedAfterStaying);
        Assert.Equal(["confirm:" + Message, "confirm:" + Message], asked);
    }

    [Fact]
    public async Task Of_two_guarded_forms_the_one_that_was_typed_into_asks_with_its_own_message()
    {
        await using var session = await HookSession.OpenAsync<LeaveGuardPage>(playwright);
        var page = session.Page;
        var asked = Answer(page, stay: true);

        await page.FillAsync("#tag", "urgent");
        await page.ClickAsync("#to-list");
        await page.ClickAsync("#count");

        await Expect(page.Locator("#counted")).ToHaveTextAsync("count=1");
        Assert.Equal(["confirm:Drop the tag?"], asked);
    }

    [Fact]
    public async Task A_reload_shows_the_browsers_own_prompt_only_while_a_form_holds_unsaved_edits()
    {
        await using var session = await HookSession.OpenAsync<LeaveGuardPage>(playwright);
        var page = session.Page;
        var asked = Answer(page, stay: true);

        await session.HooksLoadedAsync();
        var promptedUntouched = await page.EvaluateAsync<bool>(Unloads);
        await page.FillAsync("#title", "Minutes");
        var promptedUnsaved = await page.EvaluateAsync<bool>(Unloads);
        await page.EvaluateAsync("() => { location.reload(); }");
        await AskedAsync(asked, times: 1);

        Assert.False(promptedUntouched);
        Assert.True(promptedUnsaved);
        Assert.Equal(["beforeunload:"], asked);
        await Expect(page.Locator("#title")).ToHaveValueAsync("Minutes");
    }

    // Whether a `beforeunload` raised now would be cancelled, which is what makes a browser show its prompt.
    private const string Unloads = "() => !window.dispatchEvent(new Event('beforeunload', {cancelable: true}))";

    // Whether the page's guard would ask right now, read from the answer it gives the hosts.
    private static Task<bool> UnsavedAsync(IPage page) =>
        page.EvaluateAsync<bool>("() => { const ask = window.confirm; let asked = false; window.confirm = () => { asked = true; return true; }; window.__raskHookSeam.leave(); window.confirm = ask; return asked; }");

    // Answers every dialog the page raises — staying or leaving, as `stay` says when it is raised — and
    // records each as "<type>:<message>".
    private static List<string> Answer(IPage page, bool stay) => Answer(page, () => stay);

    private static List<string> Answer(IPage page, Func<bool> stay)
    {
        List<string> asked = [];
        page.Dialog += (_, dialog) =>
        {
            asked.Add(dialog.Type + ":" + dialog.Message);
            _ = stay() ? dialog.DismissAsync() : dialog.AcceptAsync();
        };
        return asked;
    }

    private static async Task AskedAsync(List<string> asked, int times)
    {
        for (var waited = 0; asked.Count < times && waited < 5_000; waited += 25)
        {
            await Task.Delay(25);
        }
    }
}

internal sealed class Minutes
{
    [Required]
    public string Title { get; set; } = "";

    public string Body { get; set; } = "";
}

internal sealed class Tagging
{
    public string Tag { get; set; } = "";
}

/// <summary>Two guarded forms at <c>/</c>, and two other paths to leave them for.</summary>
public sealed partial class LeaveGuardPage(RouteState route) : Component
{
    private readonly Minutes _minutes = new();
    private readonly Tagging _tagging = new();
    private string _saved = "";
    private int _refused;
    private int _count;

    protected override Component? HeadAssets => Markup.Title["leave guard"];

    protected override string? HtmlLang => "en";

    protected override Component? Render() =>
    [
        P.Id("path")[$"path={route.Path}"],
        P.Id("saved")[$"saved={_saved}"],
        P.Id("refused")[$"refused={_refused}"],
        P.Id("counted")[$"count={_count}"],
        Button.Id("count").OnClick(() => _count++)["count"],
        NavLink.Href("/").Id("to-form")["form"],
        NavLink.Href("/list").Id("to-list")["list"],
        NavLink.Href("/other").Id("to-other")["other"],
        string.Equals(route.Path, "/", StringComparison.Ordinal)
            ? Div[
                Form.Model(_minutes).OnSubmit(Save).OnInvalidSubmit(_ => _refused++).NoValidate(true).ConfirmLeave("Leave without saving?")[
                    Input.Bind(() => _minutes.Title).Id("title"),
                    Input.Bind(() => _minutes.Body).Id("body"),
                    Button.Type(ButtonType.Submit).Id("save")["save"]
                ],
                Form.Model(_tagging).ConfirmLeave("Drop the tag?")[Input.Bind(() => _tagging.Tag).Id("tag")]
            ]
            : null
    ];

    private void Save(Minutes minutes)
    {
        _saved = minutes.Title;
        if (string.Equals(minutes.Title, "go", StringComparison.Ordinal))
        {
            Go.To("/list");
        }
    }
}
