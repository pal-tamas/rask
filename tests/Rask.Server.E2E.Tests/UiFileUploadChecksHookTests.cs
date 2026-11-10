using Microsoft.Playwright;
using Rask.Core;
using Rask.Core.Forms;
using Rask.Server.E2E.Tests.Infrastructure;
using Rask.Site.E2E.Tests.Infrastructure;
using static Microsoft.Playwright.Assertions;

#pragma warning disable RASK019 // a small test page; its <head> is not what is under test

namespace Rask.Server.E2E.Tests;

/// <summary>
///     <c>Ui.FileUpload</c> on a live SERVER page, before and while files go: a file over <c>MaxSize</c> or outside
///     <c>Accept</c> is refused in the browser and never sent, a chosen picture is previewed from the file itself
///     (and its address let go with it), and the cross beside the bar aborts the request still carrying the files.
/// </summary>
public sealed class UiFileUploadChecksHookTests(PlaywrightFixture playwright) : IClassFixture<PlaywrightFixture>
{
    private const int Limit = 100_000;

    // A real picture, one pixel square.
    private const string OnePixelPng =
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==";

    // Counts the preview addresses the page makes and lets go of, and the uploads it sends, before anything loads.
    private const string CountUrls = """
        window.made = 0; window.revoked = 0;
        const create = URL.createObjectURL, revoke = URL.revokeObjectURL;
        URL.createObjectURL = function (o) { window.made++; return create.call(URL, o); };
        URL.revokeObjectURL = function (u) { window.revoked++; return revoke.call(URL, u); };
        // How many uploads the page sent: a refused file must never be one.
        window.uploads = 0;
        const open = XMLHttpRequest.prototype.open;
        XMLHttpRequest.prototype.open = function (method, url) {
            if (String(url).indexOf('/_rask/upload') >= 0) window.uploads++;
            return open.apply(this, arguments);
        };
        """;

    public static TheoryData<int, int> Screens => new() { { 1280, 800 }, { 390, 844 } };

    private static async Task<HookSession> OpenAsync(
        PlaywrightFixture playwright, int width, int height, string? locale = null)
    {
        var session = await HookSession.OpenAsync<UploadChecksPage>(
            playwright,
            new() { ViewportSize = new() { Width = width, Height = height }, Locale = locale ?? "en-US" },
            beforeLoad: page => page.AddInitScriptAsync(CountUrls),
            cultures: locale is null ? null : o => o.SupportedCultures.Add("hu"));
        // No attribute on this page asks for the hooks bundle: what is under test is the runtime every page loads,
        // and a press answered says its session is up.
        await session.Page.ClickAsync("#count");
        await Expect(session.Page.Locator("#counted")).ToHaveTextAsync("count=1");
        return session;
    }

    private static FilePayload File(string name, string type, int size) => new() { Name = name, MimeType = type, Buffer = new byte[size] };

    private static ILocator Checked(IPage page) => page.Locator("#checked");

    private static Task ChooseAsync(IPage page, string upload, params FilePayload[] files) =>
        page.Locator($"#{upload} input[type=file]").SetInputFilesAsync(files);

    private static async Task SlowUploadsAsync(IPage page)
    {
        var cdp = await page.Context.NewCDPSessionAsync(page);
        await cdp.SendAsync("Network.enable");
        await cdp.SendAsync("Network.emulateNetworkConditions", new Dictionary<string, object>
        {
            ["offline"] = false,
            ["latency"] = 0,
            ["downloadThroughput"] = -1,
            ["uploadThroughput"] = 100_000,
        });
    }

    [Theory]
    [MemberData(nameof(Screens))]
    public async Task A_file_exactly_at_the_limit_is_sent_and_one_byte_over_is_refused_before_it_is(int width, int height)
    {
        await using var session = await OpenAsync(playwright, width, height);
        var page = session.Page;

        await ChooseAsync(page, "checked", File("at.png", "image/png", Limit));
        await Expect(page.Locator("#received")).ToHaveTextAsync("received=at.png");
        await ChooseAsync(page, "checked", File("over.png", "image/png", Limit + 1));

        await Expect(Checked(page).Locator("[data-ui-file-refusal]")).ToHaveTextAsync("over.png is larger than 98 KB.");
        await Expect(Checked(page).Locator("[data-ui-file-upload]")).ToHaveAttributeAsync("data-invalid", string.Empty);
        await Expect(Checked(page).Locator("input[type=file]")).ToHaveAttributeAsync("aria-invalid", "true");
        await Expect(page.Locator("#received")).ToHaveTextAsync("received=at.png");
        Assert.Equal(1, await page.EvaluateAsync<int>("() => window.uploads"));
    }

    [Theory]
    [MemberData(nameof(Screens))]
    public async Task A_file_of_another_kind_is_refused_by_its_type_or_its_ending_and_the_rest_of_the_choice_goes_on(int width, int height)
    {
        await using var session = await OpenAsync(playwright, width, height);
        var page = session.Page;

        await ChooseAsync(page, "checked",
            File("cat.png", "image/png", 10), File("notes.pdf", "application/pdf", 10), File("big.jpg", "image/jpeg", Limit + 1));

        await Expect(page.Locator("#received")).ToHaveTextAsync("received=cat.png");
        await Expect(Checked(page).Locator("[data-ui-file-refusal]"))
            .ToHaveTextAsync("notes.pdf is not a kind of file this takes. big.jpg is larger than 98 KB.");
        await ChooseAsync(page, "checked", File("dog.png", "image/png", 10));
        await Expect(page.Locator("#received")).ToHaveTextAsync("received=cat.png,dog.png");
        await Expect(Checked(page).Locator("[data-ui-file-refusal]")).ToHaveTextAsync("");
        await Expect(Checked(page).Locator("[data-ui-file-upload]")).Not.ToHaveAttributeAsync("data-invalid", string.Empty);
    }

    [Fact]
    public async Task A_hungarian_visitor_is_told_why_in_hungarian()
    {
        await using var session = await OpenAsync(playwright, 1280, 800, "hu-HU");
        var page = session.Page;

        await ChooseAsync(page, "checked", File("nagy.png", "image/png", Limit + 1), File("szamla.pdf", "application/pdf", 10));

        await Expect(Checked(page).Locator("[data-ui-file-refusal]")).ToHaveTextAsync(
            "A(z) nagy.png túl nagy: legfeljebb 98 KB lehet. A(z) szamla.pdf nem választható: ilyen típusú fájlt ez a mező nem fogad.");
        await Expect(page.Locator("#received")).ToHaveTextAsync("received=");
    }

    [Theory]
    [MemberData(nameof(Screens))]
    public async Task A_chosen_picture_is_previewed_from_the_file_and_its_address_goes_with_its_item(int width, int height)
    {
        await using var session = await OpenAsync(playwright, width, height);
        var page = session.Page;

        await ChooseAsync(page, "checked",
            new FilePayload { Name = "dot.png", MimeType = "image/png", Buffer = Convert.FromBase64String(OnePixelPng) });
        var preview = page.Locator("#items [data-ui-file-item] [data-slot='image'] img");
        await Expect(preview).ToHaveAttributeAsync("src", new System.Text.RegularExpressions.Regex("^blob:"));
        await Expect(preview).ToHaveJSPropertyAsync("naturalWidth", 1);
        await page.ClickAsync("#count"); // a render of the page that changes nothing keeps the same address
        await Expect(page.Locator("#counted")).ToHaveTextAsync("count=2");
        Assert.Equal(1, await page.EvaluateAsync<int>("() => window.made"));

        await page.GetByRole(Microsoft.Playwright.AriaRole.Button, new() { Name = "Remove file: dot.png" }).ClickAsync();

        await Expect(page.Locator("#items [data-ui-file-item]")).ToHaveCountAsync(0);
        await page.WaitForFunctionAsync("() => window.revoked === 1");
        Assert.Equal(1, await page.EvaluateAsync<int>("() => window.made"));
    }

    [Fact]
    public async Task A_chosen_file_that_is_no_picture_keeps_its_icon()
    {
        await using var session = await OpenAsync(playwright, 1280, 800);
        var page = session.Page;

        await ChooseAsync(page, "plain", File("film.bin", "application/octet-stream", 10));

        await Expect(page.Locator("#received")).ToHaveTextAsync("received=film.bin");
        await Expect(page.Locator("#items [data-ui-file-item] img")).ToHaveCountAsync(0);
        Assert.Equal(0, await page.EvaluateAsync<int>("() => window.made"));
    }

    [Theory]
    [MemberData(nameof(Screens))]
    public async Task The_cross_beside_the_bar_aborts_the_request_and_the_same_file_can_be_sent_again(int width, int height)
    {
        await using var session = await OpenAsync(playwright, width, height);
        var page = session.Page;
        await SlowUploadsAsync(page);
        var plain = page.Locator("#plain [data-ui-file-upload]");

        await ChooseAsync(page, "plain", File("film.bin", "application/octet-stream", 600_000));
        await Expect(plain).ToHaveAttributeAsync("data-loading", string.Empty);
        var cancel = page.Locator("#plain [data-rask-upload-cancel]");
        await Expect(cancel).ToBeVisibleAsync();
        await cancel.ClickAsync();

        await Expect(plain).Not.ToHaveAttributeAsync("data-loading", string.Empty);
        await Expect(cancel).ToBeHiddenAsync();
        await Expect(page.Locator("#plain input[type=file]")).ToHaveValueAsync("");
        await page.WaitForTimeoutAsync(500);
        await Expect(page.Locator("#received")).ToHaveTextAsync("received=");
        Assert.Empty(session.PageErrors);

        await ChooseAsync(page, "plain", File("film.bin", "application/octet-stream", 50_000));
        await Expect(page.Locator("#received")).ToHaveTextAsync("received=film.bin", new() { Timeout = 30_000 });
    }

    [Fact]
    public async Task Calling_one_of_two_uploads_off_leaves_the_other_going()
    {
        await using var session = await OpenAsync(playwright, 1280, 800);
        var page = session.Page;
        await SlowUploadsAsync(page);

        await ChooseAsync(page, "plain", File("film.bin", "application/octet-stream", 400_000));
        await ChooseAsync(page, "checked", File("cat.png", "image/png", Limit));
        await Expect(page.Locator("#checked [data-ui-file-upload]")).ToHaveAttributeAsync("data-loading", string.Empty);
        await page.Locator("#plain [data-rask-upload-cancel]").ClickAsync();

        await Expect(page.Locator("#received")).ToHaveTextAsync("received=cat.png", new() { Timeout = 30_000 });
        await Expect(page.Locator("#plain [data-ui-file-upload]")).Not.ToHaveAttributeAsync("data-loading", string.Empty);
        await page.WaitForTimeoutAsync(500);
        await Expect(page.Locator("#received")).ToHaveTextAsync("received=cat.png");
    }
}

/// <summary>Two uploads with bars: one that checks what it takes and previews it, and one that takes anything.</summary>
public sealed partial class UploadChecksPage : Component
{
    private readonly List<IRaskFile> _files = [];
    private int _count;

    protected override Component? HeadAssets =>
    [
        Markup.Title["upload checks"],
        Markup.Style[Raw.Value(UiStylesheet.Css)],
    ];

    protected override string? HtmlLang => "en";

    protected override Component? Render() =>
    [
        P.Id("received")[$"received={string.Join(",", _files.Select(file => file.Name))}"],
        P.Id("counted")[$"count={_count}"],
        Button.Id("count").OnClick(() => _count++)["count"],
        Div.Id("checked").Style("max-width:420px")[
            Ui.FileUpload.Id("checked-input").Multiple().Accept("image/*").MaxSize(Limit).OnFiles(Keep)[
                Ui.FileUploadDropzone.Heading("Drop photos").Text("Pictures up to 100 kB").WithProgress().Inline()
            ]
        ],
        Div.Id("plain").Style("max-width:420px")[
            Ui.FileUpload.Id("plain-input").OnFiles(Keep)[
                Ui.FileUploadDropzone.Heading("Drop anything").Text("Anything").WithProgress().Inline()
            ]
        ],
        Div.Id("items")[
            _files.Select((file, index) => Ui.FileItem.Key(file.Name + index).Heading(file.Name).Size(file.Size).Preview(file)
                .Actions(Ui.FileItemRemove.AriaLabel("Remove file: " + file.Name).OnClick(() => _files.RemoveAt(index))))
        ]
    ];

    private const long Limit = 100_000;

    private void Keep(IReadOnlyList<IRaskFile> files) => _files.AddRange(files);
}
