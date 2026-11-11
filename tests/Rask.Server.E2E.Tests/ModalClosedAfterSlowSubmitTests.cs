using System.Globalization;
using Microsoft.Playwright;
using Rask.Core;
using Rask.Server.E2E.Tests.Infrastructure;
using Rask.Site.E2E.Tests.Infrastructure;
using static Microsoft.Playwright.Assertions;

#pragma warning disable RASK019 // a small test page; its <head> is not what is under test

namespace Rask.Server.E2E.Tests;

/// <summary>
///     A state-driven <c>Ui.Modal</c> holding a form whose submit awaits a slow job, then closes the modal and raises a
///     toast in the same render — eszaf's "Szétosztás" dialog, which now and then stayed open in production after the
///     server had closed it. Stressed: a slow processor, a slow line, many runs.
/// </summary>
public sealed class ModalClosedAfterSlowSubmitTests(PlaywrightFixture playwright) : IClassFixture<PlaywrightFixture>
{
    private const string Modal = "dialog[data-rask-modal-open]";

    [Theory]
    [InlineData(1280, 900, 4, 40)]
    [InlineData(1280, 900, 6, 120)]
    [InlineData(390, 844, 4, 40)]
    [InlineData(390, 844, 6, 120)]
    public async Task A_modal_the_server_closes_after_a_slow_submit_closes_in_the_browser_every_time(int width, int height, int cpu, int latency)
    {
        await using var session = await HookSession.OpenAsync<SlowSubmitModalPage>(
            playwright, new() { ViewportSize = new() { Width = width, Height = height } });
        var page = session.Page;
        await session.HooksLoadedAsync();
        var cdp = await page.Context.NewCDPSessionAsync(page);
        await cdp.SendAsync("Emulation.setCPUThrottlingRate", new Dictionary<string, object> { ["rate"] = cpu });
        await cdp.SendAsync("Network.enable");
        await cdp.SendAsync("Network.emulateNetworkConditions", new Dictionary<string, object>
        {
            ["offline"] = false,
            ["latency"] = latency,
            ["downloadThroughput"] = -1,
            ["uploadThroughput"] = -1,
        });
        var random = new Random(width + cpu + latency);

        for (var run = 0; run < 12; run++)
        {
            SlowSubmitModalPage.JobMilliseconds = 300 + random.Next(1500);
            await page.ClickAsync("#open");
            await Expect(page.Locator(Modal)).ToBeVisibleAsync();
            await page.ClickAsync("#company [data-ui-select-button]");
            await page.Locator("#company [data-ui-option]").Nth(random.Next(3)).ClickAsync();
            await page.WaitForTimeoutAsync(random.Next(200));

            await page.ClickAsync("#run");

            await Expect(page.Locator("#runs")).ToHaveTextAsync((run + 1).ToString(CultureInfo.InvariantCulture), new() { Timeout = 10_000 });
            await Expect(page.Locator(Modal)).ToHaveAttributeAsync("data-rask-modal-open", "false");
            try
            {
                await Expect(page.Locator(Modal)).ToBeHiddenAsync(new() { Timeout = 3_000 });
            }
            catch (PlaywrightException e)
            {
                Assert.Fail($"run {run}: the server closed the modal and the browser kept it open. {e.Message}");
            }
        }

        Assert.Empty(session.PageErrors);
    }
}

/// <summary>The reported page: a button opens the modal; its form picks a company and runs a slow job that closes it and toasts.</summary>
public sealed partial class SlowSubmitModalPage : Component
{
    private static volatile int _jobMilliseconds = 1000;

    private Distribution _model = new();
    private bool _open;
    private int _runs;

    /// <summary>How long the next submit's job takes.</summary>
    public static int JobMilliseconds
    {
        get => _jobMilliseconds;
        set => _jobMilliseconds = value;
    }

    protected override Component? HeadAssets => Markup.Title["slow submit modal"];

    protected override string? HtmlLang => "en";

    protected override Component? Render() => Div[
        Main[
            P.Id("runs")[_runs.ToString(CultureInfo.InvariantCulture)],
            Ui.Button.Id("open").OnClick(() => { _model = new(); _open = true; })["Distribute"],
            Ui.Modal.Key("distribution").Class("min-w-[22rem] md:w-[28rem]").Open(_open).OnClose(() => _open = false)[
                Form.Model(_model).OnSubmit(Distribute).Class("space-y-6")[submit =>
                [
                    Div[Ui.Heading.Level(2).Lg.Class("pe-8")["Distribute"], Ui.Text.Class("mt-2")["Recomputes the prices."]],
                    Div.Id("company")[Ui.Field[
                        Ui.Label.Badge("Required")["Company"],
                        Ui.Select.Bind(() => _model.Company).Listbox.Searchable(true).Placeholder("Choose").ShowValidation(false)
                            .Validate(id => id is null ? ["Required"] : [])[
                            Ui.SelectOption.Key("c:1").Value((int?)1)["One"],
                            Ui.SelectOption.Key("c:2").Value((int?)2)["Two"],
                            Ui.SelectOption.Key("c:3").Value((int?)3)["Three"]
                        ],
                        Ui.Error
                    ]],
                    Div.Class("flex gap-2")[
                        Ui.Spacer,
                        Ui.ModalClose[Ui.Button.Key("cancel").Ghost.Disabled(submit.Submitting)["Cancel"]],
                        Ui.Button.Key("run").Primary.Submit.Id("run")["Run"]
                    ]
                ]]
            ]
        ],
        Ui.Toast
    ];

    private async Task Distribute(Distribution model)
    {
        await Task.Delay(JobMilliseconds);
        _runs++;
        _open = false;
        Toast.Success("Distributed.");
    }

    /// <summary>What the form holds.</summary>
    public sealed class Distribution
    {
        public int? Company { get; set; }
    }
}
