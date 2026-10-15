using Microsoft.Playwright;
using Rask.Core;
using Rask.Server.E2E.Tests.Infrastructure;
using Rask.Site.E2E.Tests.Infrastructure;
using static Microsoft.Playwright.Assertions;

#pragma warning disable RASK019 // a small test page; its <head> is not what is under test

namespace Rask.Server.E2E.Tests;

/// <summary>
///     A field whose event was sent says <c>data-loading</c> until that event's answer — Livewire puts it on any element
///     that triggered a request, a <c>wire:model.live</c> input included — and nothing else about it changes.
/// </summary>
/// <remarks>
///     The handler waits long enough to be looked at. The WebAssembly host marks from the same module with its
///     dispatch promise: <c>FieldBusyMarkWasmTests</c> in <c>Rask.Site.E2E.Tests</c>.
/// </remarks>
public sealed class FieldBusyMarkTests(PlaywrightFixture playwright) : IClassFixture<PlaywrightFixture>
{
    [Fact]
    public async Task A_live_field_is_marked_while_its_value_is_being_answered_and_clear_after_it()
    {
        await using var session = await HookSession.OpenAsync<FieldBusyPage>(playwright);
        var page = session.Page;

        await page.FillAsync("#live", "ada");

        await Expect(page.Locator("#live")).ToHaveAttributeAsync("data-loading", "");
        await Expect(page.Locator("#live")).Not.ToHaveAttributeAsync("aria-busy", new System.Text.RegularExpressions.Regex(".*"));
        await Expect(page.Locator("#heard")).ToHaveTextAsync("heard=ada", new() { Timeout = 10_000 });
        await Expect(page.Locator("#live")).Not.ToHaveAttributeAsync("data-loading", new System.Text.RegularExpressions.Regex(".*"));
    }

    [Fact]
    public async Task A_field_that_waits_for_the_next_action_is_not_marked_by_typing()
    {
        await using var session = await HookSession.OpenAsync<FieldBusyPage>(playwright);
        var page = session.Page;

        await page.FillAsync("#waits", "ada");
        await page.WaitForTimeoutAsync(500);

        Assert.Null(await page.Locator("#waits").GetAttributeAsync("data-loading"));
    }

    [Fact]
    public async Task A_field_that_opts_out_is_not_marked()
    {
        await using var session = await HookSession.OpenAsync<FieldBusyPage>(playwright);
        var page = session.Page;

        await page.FillAsync("#quiet", "ada");
        await Expect(page.Locator("#heard")).ToHaveTextAsync("heard=ada", new() { Timeout = 10_000 });

        Assert.Null(await page.Locator("#quiet").GetAttributeAsync("data-loading"));
    }

    [Fact]
    public async Task A_second_value_sent_before_the_first_is_answered_keeps_the_mark_until_the_last()
    {
        await using var session = await HookSession.OpenAsync<FieldBusyPage>(playwright);
        var page = session.Page;

        await page.FillAsync("#live", "a");
        await Expect(page.Locator("#live")).ToHaveAttributeAsync("data-loading", "");
        await page.FillAsync("#live", "ab");

        await Expect(page.Locator("#heard")).ToHaveTextAsync("heard=ab", new() { Timeout = 15_000 });
        await Expect(page.Locator("#live")).Not.ToHaveAttributeAsync("data-loading", new System.Text.RegularExpressions.Regex(".*"));
    }
}

internal sealed class FieldBusyModel
{
    public string Live { get; set; } = "";

    public string Waits { get; set; } = "";

    public string Quiet { get; set; } = "";
}

/// <summary>A live field with a slow handler, a field that waits for the next action, and a live one that opts out.</summary>
public sealed partial class FieldBusyPage : Component
{
    private readonly FieldBusyModel _model = new();
    private string _heard = "";

    protected override Component? HeadAssets => Markup.Title["field busy"];

    protected override string? HtmlLang => "en";

    protected override Component? Render() =>
    [
        P.Id("heard")[$"heard={_heard}"],
        Input.Bind(() => _model.Live).Live().AfterBind(async v => { await Task.Delay(900); _heard = v; }).Id("live"),
        Input.Bind(() => _model.Waits).Id("waits"),
        Input.Bind(() => _model.Quiet).Live().Data("rask-loading", "off").AfterBind(async v => { await Task.Delay(900); _heard = v; }).Id("quiet")
    ];
}
