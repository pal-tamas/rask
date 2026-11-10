using Microsoft.Playwright;
using Rask.Site.E2E.Tests.Infrastructure;
using static Microsoft.Playwright.Assertions;

namespace Rask.Site.E2E.Tests;

/// <summary>
///     The dirty marks, starting a form again, and the upload's checks, preview and cancel on the WebAssembly host,
///     through the forms guide's demo and the file upload page's examples, at a desktop's width and a phone's. The
///     Server host's half is <c>FormDirtyHookTests</c> and <c>UiFileUploadChecksHookTests</c> in
///     <c>Rask.Server.E2E.Tests</c>.
/// </summary>
public sealed class FormDirtyAndUploadWasmTests(WasmExampleAppFixture app, PlaywrightFixture pw)
    : SharedSmokeTests(pw)
{
    // A real picture, one pixel square.
    private const string OnePixelPng =
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==";

    protected override string BaseUrl => app.BaseUrl;

    protected override string FixtureName => "Wasm";

    protected override string ServerLog => app.ServerLog;

    public static TheoryData<int, int> Screens => new() { { 1280, 800 }, { 390, 844 } };

    private async Task OpenFormAsync(int width, int height)
    {
        await Page.SetViewportSizeAsync(width, height);
        await Page.GotoAsync(Docs + "/guides/forms");
        await WaitForInteractiveAsync();
        var demo = Page.Locator("#fd-form");
        await Expect(demo).ToBeVisibleAsync(new() { Timeout = 30_000 });
        await demo.ScrollIntoViewIfNeededAsync();
    }

    private async Task OpenUploadsAsync(int width, int height)
    {
        await Page.SetViewportSizeAsync(width, height);
        await OpenUiPageAsync("file-upload", "File upload");
        await Page.Locator("[data-testid='ui-upload-checked']").ScrollIntoViewIfNeededAsync();
    }

    private Task Marked(string selector) => Expect(Page.Locator(selector)).ToHaveAttributeAsync("data-dirty", string.Empty);

    private Task Unmarked(string selector) => Expect(Page.Locator(selector)).Not.ToHaveAttributeAsync("data-dirty", string.Empty);

    [Theory]
    [MemberData(nameof(Screens))]
    public Task A_changed_field_and_its_form_are_marked_and_unmarked_when_put_back(int width, int height) => RunAsync(async () =>
    {
        await OpenFormAsync(width, height);
        await Unmarked("#fd-form");

        await Page.FillAsync("#fd-tank", "South");
        await Marked("#fd-tank");
        await Marked("#fd-form");
        await Expect(Page.Locator("#fd-marked")).ToBeVisibleAsync();
        await Page.FillAsync("#fd-tank", "North");

        await Unmarked("#fd-tank");
        await Unmarked("#fd-form");
        await Expect(Page.Locator("#fd-marked")).ToBeHiddenAsync();
    });

    [Theory]
    [MemberData(nameof(Screens))]
    public Task A_save_takes_the_marks_and_the_server_agrees_and_start_again_puts_the_values_back(int width, int height) => RunAsync(async () =>
    {
        await OpenFormAsync(width, height);
        await Page.FillAsync("#fd-tank", "South");
        await Page.GetByText("Urgent", new() { Exact = true }).ClickAsync(); // the box itself is drawn by the kit over an sr-only input
        await Marked("#fd-urgent");

        await Page.ClickAsync("#fd-save");
        await Expect(Page.Locator("#fd-out")).ToContainTextAsync("South, 40 l, urgent", new() { Timeout = 15_000 });
        await Unmarked("#fd-form");
        await Unmarked("#fd-urgent");
        await Expect(Page.Locator("#fd-unsaved")).ToHaveTextAsync("The server holds nothing unsaved.");
        await Page.FillAsync("#fd-litres", "55");
        await Marked("#fd-litres");
        await Page.ClickAsync("#fd-reset");

        await Expect(Page.Locator("#fd-litres")).ToHaveValueAsync("40");
        await Expect(Page.Locator("#fd-tank")).ToHaveValueAsync("North");
        await Expect(Page.Locator("#fd-urgent")).Not.ToBeCheckedAsync();
        await Unmarked("#fd-form");
        await Expect(Page.Locator("#fd-unsaved")).ToHaveTextAsync("The server holds nothing unsaved.");
    });

    [Theory]
    [MemberData(nameof(Screens))]
    public Task A_file_over_the_size_or_of_another_kind_is_refused_before_it_is_read_and_the_rest_arrive(int width, int height) => RunAsync(async () =>
    {
        await OpenUploadsAsync(width, height);
        var example = Page.Locator("[data-testid='ui-upload-checked']");

        await example.Locator("input[type=file]").SetInputFilesAsync(new[]
        {
            new FilePayload { Name = "dot.png", MimeType = "image/png", Buffer = Convert.FromBase64String(OnePixelPng) },
            new FilePayload { Name = "notes.pdf", MimeType = "application/pdf", Buffer = new byte[10] },
            new FilePayload { Name = "huge.png", MimeType = "image/png", Buffer = new byte[2 * 1024 * 1024 + 1] },
            new FilePayload { Name = "exact.png", MimeType = "image/png", Buffer = new byte[2 * 1024 * 1024] },
        });

        await Expect(example.Locator("[data-ui-file-refusal]"))
            .ToHaveTextAsync("notes.pdf is not a kind of file this takes. huge.png is larger than 2 MB.");
        await Expect(example.Locator("[data-ui-file-upload]")).ToHaveAttributeAsync("data-invalid", string.Empty);
        await Expect(example.Locator("[data-ui-file-item]")).ToHaveCountAsync(2, new() { Timeout = 15_000 });
        await Expect(example.Locator("[data-ui-file-item]").Nth(0)).ToContainTextAsync("dot.png");
        await Expect(example.Locator("[data-ui-file-item]").Nth(1)).ToContainTextAsync("exact.png");
    });

    [Theory]
    [MemberData(nameof(Screens))]
    public Task A_chosen_picture_is_previewed_from_the_file_and_its_address_goes_with_its_item(int width, int height) => RunAsync(async () =>
    {
        await Page.AddInitScriptAsync("""
            window.revoked = 0;
            const revoke = URL.revokeObjectURL;
            URL.revokeObjectURL = function (u) { window.revoked++; return revoke.call(URL, u); };
            """);
        await OpenUploadsAsync(width, height);
        var example = Page.Locator("[data-testid='ui-upload-checked']");

        await example.Locator("input[type=file]").SetInputFilesAsync(
            new FilePayload { Name = "dot.png", MimeType = "image/png", Buffer = Convert.FromBase64String(OnePixelPng) });
        var preview = example.Locator("[data-ui-file-item] [data-slot='image'] img");
        await Expect(preview).ToHaveAttributeAsync("src", new System.Text.RegularExpressions.Regex("^blob:"), new() { Timeout = 15_000 });
        await Expect(preview).ToHaveJSPropertyAsync("naturalWidth", 1);
        await example.GetByRole(AriaRole.Button, new() { Name = "Remove file: dot.png" }).ClickAsync();

        await Expect(example.Locator("[data-ui-file-item]")).ToHaveCountAsync(0);
        await Page.WaitForFunctionAsync("() => window.revoked === 1");
    });

    // Chooses nine megabytes in the progress example and presses the cross when `when` says: at once, or as the
    // handler's first chunk is read. Done in the page, so neither moment depends on how fast the test looks.
    private const string ChooseAndCancel = """
        ([example, when]) => new Promise(resolve => {
            const upload = example.querySelector('[data-ui-file-upload]');
            const input = example.querySelector('input[type=file]');
            const cancel = () => {
                const loading = upload.hasAttribute('data-loading');
                example.querySelector('[data-rask-upload-cancel]').click();
                resolve(loading);
            };
            if (when === 'reading') {
                new MutationObserver((_, watching) => {
                    const gone = upload.style.getPropertyValue('--rask-progress');
                    if (gone && gone !== '0%') { watching.disconnect(); cancel(); }
                }).observe(upload, { attributes: true, attributeFilter: ['style'] });
            }
            const files = new DataTransfer();
            files.items.add(new File([new Uint8Array(9 * 1024 * 1024)], 'film.bin'));
            input.files = files.files;
            input.dispatchEvent(new Event('change', { bubbles: true }));
            if (when === 'at once') cancel();
        })
        """;

    [Theory]
    [InlineData(1280, 800, "at once")]
    [InlineData(1280, 800, "reading")]
    [InlineData(390, 844, "at once")]
    [InlineData(390, 844, "reading")]
    public Task The_cross_beside_the_bar_cancels_the_handler_reading_the_file(int width, int height, string when) => RunAsync(async () =>
    {
        await Page.SetViewportSizeAsync(width, height);
        await OpenUiPageAsync("file-upload", "File upload");
        var example = Page.Locator("[data-testid='ui-upload-progress']");
        await example.ScrollIntoViewIfNeededAsync();
        var upload = example.Locator("[data-ui-file-upload]");
        var handle = await example.ElementHandleAsync();

        var loadingWhenPressed = await Page.EvaluateAsync<bool>(ChooseAndCancel, new object[] { handle, when });

        Assert.True(loadingWhenPressed);
        await Expect(upload).Not.ToHaveAttributeAsync("data-loading", string.Empty);
        await Expect(example.Locator("input[type=file]")).ToHaveValueAsync("");
        await Page.WaitForTimeoutAsync(1_000);
        await Expect(example.Locator("[data-ui-file-item]")).ToHaveCountAsync(0);

        // And a file again, left to finish: the cancel held nothing over.
        await example.Locator("input[type=file]").SetInputFilesAsync(
            new FilePayload { Name = "film.bin", MimeType = "application/octet-stream", Buffer = new byte[64 * 1024] });
        await Expect(example.Locator("[data-ui-file-item]")).ToHaveCountAsync(1, new() { Timeout = 15_000 });
    });
}
