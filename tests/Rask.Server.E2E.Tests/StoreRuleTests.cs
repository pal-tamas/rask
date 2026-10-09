using System.Text.RegularExpressions;
using Microsoft.Playwright;
using Rask.Core;
using Rask.Core.Forms;
using Rask.Server.E2E.Tests.Infrastructure;
using Rask.Site.E2E.Tests.Infrastructure;
using Rask.Wire;
using static Microsoft.Playwright.Assertions;

#pragma warning disable RASK019 // a small test page; its <head> is not what is under test

namespace Rask.Server.E2E.Tests;

/// <summary>
///     A rule the store owns, with no step on the field, in a real browser over the real socket: nothing is
///     sent while the reader types a taken name, one message at the pause brings the store's answer under the
///     field, the first key of a correction takes it away, and a free name is saved.
/// </summary>
/// <remarks>
///     The store is a fake registered for the page's model, as a data layer registers its own. The session
///     fails the journey on any error the page did not catch.
/// </remarks>
public sealed partial class StoreRuleTests(PlaywrightFixture playwright) : IClassFixture<PlaywrightFixture>
{
    private const string Taken = "Ilyen néven már létezik viszonylat.";

    [Fact]
    public async Task A_taken_name_is_told_at_the_pause_cleared_at_the_first_key_and_a_free_one_is_saved()
    {
        await using var session = await HookSession.OpenAsync<StoreRulePage>(playwright, beforeLoad: CountFrames);
        var page = session.Page;
        await page.ClickAsync("#ready");
        await Expect(page.Locator("#is-ready")).ToHaveTextAsync("ready");

        var before = await SentAsync(page);
        await page.Locator("#name").PressSequentiallyAsync("Wien", new() { Delay = 30 });
        var whileTyping = await SentAsync(page) - before;
        await Expect(page.Locator("#name-error")).ToHaveTextAsync(Taken);
        var atThePause = await SentAsync(page) - before;
        var askedAtThePause = await page.Locator("#asked").TextContentAsync();
        await page.Locator("#name").PressSequentiallyAsync("e");
        await Expect(page.Locator("#name-error")).ToHaveCountAsync(0, new() { Timeout = StoreRulePage.PauseMilliseconds / 2 });
        var atTheFirstKey = await SentAsync(page) - before;
        await page.Locator("#name").PressSequentiallyAsync("rwald", new() { Delay = 30 });
        await Expect(page.Locator("#model")).ToHaveTextAsync("name=Wienerwald");
        await page.ClickAsync("#save");

        await Expect(page.Locator("#saved")).ToHaveTextAsync("saved=Wienerwald");
        await Expect(page.Locator("#asked")).ToHaveTextAsync(FieldsThenTheWholeModel());
        await Expect(page.Locator("#name-error")).ToHaveCountAsync(0);
        Assert.Equal(0, whileTyping);
        Assert.Equal(1, atThePause);
        Assert.Equal("asked=Name", askedAtThePause);
        Assert.Equal(2, atTheFirstKey);
    }

    [Fact]
    public async Task Saving_a_taken_name_from_the_keyboard_is_stopped_before_the_save_and_told_under_the_field()
    {
        await using var session = await HookSession.OpenAsync<StoreRulePage>(playwright);
        var page = session.Page;
        await page.ClickAsync("#ready");
        await Expect(page.Locator("#is-ready")).ToHaveTextAsync("ready");

        await page.Locator("#name").PressSequentiallyAsync("Wien");
        await page.Locator("#name").PressAsync("Enter");

        await Expect(page.Locator("#name-error")).ToHaveTextAsync(Taken);
        await Expect(page.Locator("#saved")).ToHaveTextAsync("saved=|saves=0");
    }

    // The pause after the first key of the correction may or may not have passed before the rest was typed.
    [GeneratedRegex("^asked=Name(,Name){1,2},all$")]
    private static partial Regex FieldsThenTheWholeModel();

    // The socket's outgoing frames, counted in the page, so a count read "now" is the count now.
    private static Task CountFrames(IPage page) => page.AddInitScriptAsync(
        """
        window.__sent = 0;
        const send = WebSocket.prototype.send;
        WebSocket.prototype.send = function (data) { window.__sent++; return send.call(this, data); };
        """);

    private static Task<int> SentAsync(IPage page) => page.EvaluateAsync<int>("() => window.__sent");
}

internal sealed class Destination
{
    public string Name { get; set; } = "";

    internal List<string> Asked { get; } = [];
}

// What a data layer registers for a model with a unique index on Name: here, "Wien" is taken.
internal sealed class DestinationRules : IStoreRules
{
    internal static readonly DestinationRules Instance = new();

    public async ValueTask<IReadOnlyList<FieldFailure>> Check(object model, string? field, CancellationToken cancellationToken)
    {
        var destination = (Destination)model;
        destination.Asked.Add(field ?? "all");
        await Task.Delay(20, cancellationToken);

        return string.Equals(destination.Name, "Wien", StringComparison.Ordinal)
            ? [new FieldFailure("Ilyen néven már létezik viszonylat.", ["Name"], Source: "IX_Destination_Name")]
            : [];
    }
}

/// <summary>A debounced name with no rule written on it, over a model a store registered a unique name for.</summary>
public sealed partial class StoreRulePage : Component
{
    internal const int PauseMilliseconds = 400;

    private readonly Destination _destination = new();
    private string _saved = "";
    private int _saves;
    private bool _ready;

    static StoreRulePage() => RaskValidation.RegisterStoreRules(typeof(Destination), _ => DestinationRules.Instance);

    protected override Component? HeadAssets => Markup.Title["store rule"];

    protected override string? HtmlLang => "en";

    protected override Component? Render() =>
    [
        P.Id("saved")[_saves == 0 ? "saved=|saves=0" : $"saved={_saved}"],
        P.Id("model")[$"name={_destination.Name}"],
        P.Id("asked")[$"asked={string.Join(',', _destination.Asked)}"],
        P.Id("is-ready")[_ready ? "ready" : "loading"],
        Button.Id("ready").OnClick(() => _ready = true)["ready"],
        Form.Model(_destination).OnSubmit(Save)[
            Input.Bind(() => _destination.Name).Id("name").Debounce(PauseMilliseconds.Milliseconds),
            Validation.Message.Template(messages => P.Id("name-error")[messages[0]]).For(() => _destination.Name),
            Button.Type(ButtonType.Submit).Id("save")["save"]
        ]
    ];

    private void Save(Destination destination)
    {
        _saves++;
        _saved = destination.Name;
    }
}
