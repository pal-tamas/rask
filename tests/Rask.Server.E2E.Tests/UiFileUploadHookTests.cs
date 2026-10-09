using Rask.Core;
using Rask.Core.Forms;
using Rask.Server.E2E.Tests.Infrastructure;
using Rask.Site.E2E.Tests.Infrastructure;
using static Microsoft.Playwright.Assertions;

#pragma warning disable RASK019 // a small test page; its <head> is not what is under test

namespace Rask.Server.E2E.Tests;

/// <summary>
///     <c>Ui.FileUpload</c> on a live SERVER page: the upload is a real request, and the dropzone's bar is as
///     wide as the part of it that has gone.
/// </summary>
/// <remarks>
///     <c>RuntimeHookGestureTests</c> pins the hook against <c>data-rask-loading</c> written by hand. This is the
///     seam on the other side of it: the component writes the attribute, the kit's sheet hands the runtime's
///     <c>--rask-progress</c> on as the variable the bar reads, and the request is slowed so that what lies
///     between nothing and everything can be seen.
/// </remarks>
public sealed class UiFileUploadHookTests(PlaywrightFixture playwright) : IClassFixture<PlaywrightFixture>
{
    private const string Watch = """
        () => {
            window.seen = [];
            const upload = document.querySelector('[data-ui-file-upload]');
            const fill = upload.querySelector('[data-ui-file-upload-dropzone] .rounded-full > .rounded-full');
            const track = fill.parentElement;
            const note = () => window.seen.push([
                upload.hasAttribute('data-loading') ? 'loading' : 'idle',
                upload.style.getPropertyValue('--rask-progress'),
                getComputedStyle(fill).getPropertyValue('--ui-file-upload-progress').trim(),
                Math.round(100 * fill.getBoundingClientRect().width / track.getBoundingClientRect().width),
            ].join(' '));
            new MutationObserver(note).observe(upload, { attributes: true });
        }
        """;

    [Fact]
    public async Task The_bar_fills_as_the_request_goes_and_the_upload_rests_once_the_page_has_its_files()
    {
        await using var session = await HookSession.OpenAsync<FileUploadHookPage>(playwright);
        var page = session.Page;
        var upload = page.Locator("[data-ui-file-upload]");
        var cdp = await page.Context.NewCDPSessionAsync(page);
        // 600 kB at 150 kB a second: some four seconds of request, and a progress event every few tens of ms.
        await cdp.SendAsync("Network.enable");
        await cdp.SendAsync("Network.emulateNetworkConditions", new Dictionary<string, object>
        {
            ["offline"] = false,
            ["latency"] = 0,
            ["downloadThroughput"] = -1,
            ["uploadThroughput"] = 150_000,
        });
        await page.EvaluateAsync(Watch);

        await upload.Locator("input[type=file]").SetInputFilesAsync(
            new Microsoft.Playwright.FilePayload { Name = "film.bin", MimeType = "application/octet-stream", Buffer = new byte[600_000] });
        await Expect(upload).ToHaveAttributeAsync("data-loading", string.Empty);
        await Expect(page.Locator("#received")).ToHaveTextAsync("received=600000", new() { Timeout = 30_000 });
        await Expect(upload).Not.ToHaveAttributeAsync("data-loading", string.Empty);
        var seen = await page.EvaluateAsync<string[]>("() => window.seen");
        var percents = seen.Where(s => s.StartsWith("loading", StringComparison.Ordinal)).Select(Percent).ToList();

        // Each note: the mark, the runtime's variable, the kit's variable on the bar, the bar's width in percent.
        Assert.Contains("loading 0% 0% 0", seen);
        Assert.Contains("loading 100% 100% 100", seen);
        Assert.True(percents.Count(p => p is > 0 and < 100) >= 3, "no progress between nothing and everything: " + string.Join(" | ", seen));
        Assert.Equal(percents.Order(), percents);
        Assert.All(seen.Where(s => s.StartsWith("loading", StringComparison.Ordinal)), s =>
        {
            var parts = s.Split(' ');
            Assert.Equal(parts[1], parts[2]);
            Assert.InRange(int.Parse(parts[3], System.Globalization.CultureInfo.InvariantCulture), Percent(s) - 1, Percent(s) + 1);
        });
        Assert.Equal("0%", await upload.EvaluateAsync<string>("u => getComputedStyle(u).getPropertyValue('--ui-file-upload-progress').trim()"));
    }

    private static int Percent(string note) =>
        int.Parse(note.Split(' ')[1].TrimEnd('%'), System.Globalization.CultureInfo.InvariantCulture);
}

/// <summary>An upload with a bar, drawn with the kit's own sheet.</summary>
public sealed partial class FileUploadHookPage : Component
{
    private long _received;

    protected override Component? HeadAssets =>
    [
        Markup.Title["file upload hook"],
        // Raw, because CSS is not HTML: encoding it would break every selector containing > or &.
        Markup.Style[Raw.Value(UiStylesheet.Css)],
    ];

    protected override string? HtmlLang => "en";

    protected override Component? Render() =>
    [
        P.Id("received")[$"received={_received}"],
        Div.Style("width:420px")[
            Ui.FileUpload.Multiple().OnFiles(Count)[
                Ui.FileUploadDropzone.Heading("Drop files or click to browse").Text("Anything").WithProgress().Inline()
            ]
        ]
    ];

    private void Count(IReadOnlyList<IRaskFile> files) => _received = files.Sum(file => file.Size);
}
