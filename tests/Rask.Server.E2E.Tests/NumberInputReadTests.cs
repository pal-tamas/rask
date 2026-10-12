using System.ComponentModel.DataAnnotations;
using System.Text;
using Microsoft.Playwright;
using Rask.Core;
using Rask.Server.E2E.Tests.Infrastructure;
using Rask.Site.E2E.Tests.Infrastructure;

#pragma warning disable RASK019 // a small test page; its <head> is not what is under test

namespace Rask.Server.E2E.Tests;

/// <summary>
///     What a bound number, date or time input sends for text it cannot hold, and what the page says of it, in a
///     real browser over the real socket.
/// </summary>
public sealed class NumberInputReadTests(PlaywrightFixture playwright) : IClassFixture<PlaywrightFixture>
{
    private const string Record =
        """
        window.__frames = [];
        const send = WebSocket.prototype.send;
        WebSocket.prototype.send = function (data) { window.__frames.push(String(data)); return send.call(this, data); };
        """;

    [Fact]
    public async Task Probe_what_each_field_sends_and_says_for_text_it_cannot_hold()
    {
        (string Field, string Typed)[] cases =
        [
            ("weight", "1.5"), ("weight", "1e3"), ("weight", "abc"), ("weight", "-"), ("weight", "99999999999"),
            ("weight", "1e400"), ("weight", "12"),
            ("count", "1.5"), ("count", "-"), ("count", "99999999999999999999"),
            ("price", "1.5"), ("price", "1e3"), ("price", "-"), ("price", "1e400"),
            ("ratio", "1e3"), ("ratio", "-"), ("ratio", "1e400"),
            ("day", "12"), ("at", "1"), ("when", "12"),
            ("live", "1.5"), ("live", "-"), ("blur", "1.5"), ("blur", "-"),
        ];
        var table = new StringBuilder();

        foreach (var (field, typed) in cases)
        {
            await using var session = await HookSession.OpenAsync<NumberReadPage>(playwright, beforeLoad: page => page.AddInitScriptAsync(Record));
            var page = session.Page;
            await page.WaitForFunctionAsync("() => window.__frames.length > 0 || document.querySelector('[data-rask-on-submit]')");
            await page.Locator("#" + field).PressSequentiallyAsync(typed);
            await page.WaitForTimeoutAsync(400);
            var seen = await page.Locator("#" + field).EvaluateAsync<string>(
                "e => `value=${JSON.stringify(e.value)} bad=${e.validity.badInput} step=${e.validity.stepMismatch}`");
            await page.EvaluateAsync("() => window.__frames = []");
            await page.ClickAsync("#save");
            await page.WaitForTimeoutAsync(400);
            var frames = await page.EvaluateAsync<string[]>("() => window.__frames");
            var model = await page.Locator("#model").TextContentAsync();
            var saved = await page.Locator("#saved").TextContentAsync();
            var error = await page.Locator("#" + field + "-error").TextContentAsync();
            table.AppendLine(System.Globalization.CultureInfo.InvariantCulture,
                $"{field} <- {typed}: {seen} | sent {string.Join(' ', frames)} | {model} | {saved} | error={error}");
        }

        await File.WriteAllTextAsync(Path.Combine(Path.GetTempPath(), "rask-number-probe.txt"), table.ToString(), TestContext.Current.CancellationToken);
        Assert.NotEqual(0, table.Length);
    }
}

/// <summary>A form of every kind of field a number, a date or a time is typed into.</summary>
public sealed partial class NumberReadPage : Component
{
    private readonly Tank _tank = new();
    private string _saved = "";

    protected override Component? HeadAssets => Markup.Title["number read"];

    protected override string? HtmlLang => "en";

    protected override Component? Render() =>
    [
        P.Id("saved")[$"saved={_saved}"],
        P.Id("model")[Describe()],
        Form.Model(_tank).OnSubmit(Save)[
            Ui.Input.Bind(() => _tank.Weight).Label("Weight").Id("weight"),
            Ui.Input.Bind(() => _tank.Count).Label("Count").Id("count"),
            Ui.Input.Bind(() => _tank.Price).Label("Price").Id("price"),
            Ui.Input.Bind(() => _tank.Ratio).Label("Ratio").Id("ratio"),
            Ui.Input.Bind(() => _tank.Day).Label("Day").Id("day"),
            Ui.Input.Bind(() => _tank.At).Label("At").Id("at"),
            Ui.Input.Bind(() => _tank.When).Label("When").Id("when"),
            Ui.Input.Bind(() => _tank.Live).Live().Label("Live").Id("live"),
            Ui.Input.Bind(() => _tank.Blur).Blur().Label("Blur").Id("blur"),
            Ui.Button.Submit.Id("save")["Save"]
        ]
    ];

    private string Describe() => FormattableString.Invariant(
        $"weight={_tank.Weight} count={_tank.Count} price={_tank.Price} ratio={_tank.Ratio} day={_tank.Day:yyyy-MM-dd} at={_tank.At:HH:mm} when={_tank.When:s} live={_tank.Live} blur={_tank.Blur}");

    private void Save(Tank tank) => _saved = Describe();

    private sealed class Tank
    {
        [Required]
        public int? Weight { get; set; }

        public long Count { get; set; }

        public decimal? Price { get; set; }

        public double Ratio { get; set; }

        public DateOnly? Day { get; set; }

        public TimeOnly? At { get; set; }

        public DateTime? When { get; set; }

        public int? Live { get; set; }

        public int? Blur { get; set; }
    }
}
