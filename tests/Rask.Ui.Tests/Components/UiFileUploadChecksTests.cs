using Rask.Core;
using Rask.Core.Forms;

namespace Rask.UiTests.Components;

/// <summary>
///     What a file upload hands the browser so a file it would refuse is refused before any of it is sent, what its
///     dropzone draws to call an upload off, and how a file item asks for the preview of a chosen image.
/// </summary>
/// <remarks>The browser's half is <c>DirtyAndUploadClientTests</c> in <c>Rask.Core.Tests</c>.</remarks>
public partial class UiFileUploadChecksTests : global::Rask.Core.RaskMarkup
{
    private sealed class Chosen(string name, long size, string type) : IRaskFile
    {
        public string Name => name;

        public long Size => size;

        public string ContentType => type;

        public DateTimeOffset LastModified => DateTimeOffset.FromUnixTimeMilliseconds(1_700_000_000_000);

        public Stream OpenReadStream(long maxAllowedSize = 512 * 1024, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("A preview reads nothing.");
    }

    private static Component Dropzone() => Ui.FileUploadDropzone.Heading("Drop files here").Text("Up to 10 MB").WithProgress();

    [Fact]
    public void A_largest_size_is_handed_to_the_browser_with_the_sentence_that_names_it()
    {
        var upload = Ui.FileUpload.Id("photos").MaxSize(5.Megabytes)[Dropzone()];

        var html = upload.ToHtml();

        Assert.Contains("data-rask-max-size=\"5242880\"", html, StringComparison.Ordinal);
        Assert.Contains("data-rask-too-large=\"{0} is larger than 5 MB.\"", html, StringComparison.Ordinal);
        Assert.Contains("data-rask-refusal=\"photos-refused\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("data-rask-not-accepted", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Accept_is_checked_in_the_browser_too_and_says_so_in_its_own_sentence()
    {
        var upload = Ui.FileUpload.Id("photos").Accept("image/*")[Dropzone()];

        var html = upload.ToHtml();

        Assert.Contains("accept=\"image/*\"", html, StringComparison.Ordinal);
        Assert.Contains("data-rask-not-accepted=\"{0} is not a kind of file this takes.\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("data-rask-max-size", html, StringComparison.Ordinal);
    }

    [Fact]
    public void The_sentence_goes_in_an_empty_live_region_under_the_upload_that_no_render_writes_into()
    {
        var upload = Ui.FileUpload.Id("photos").MaxSize(1.Megabyte)[Dropzone()];

        var html = upload.ToHtml();

        var region = html[html.IndexOf("<div id=\"photos-refused\"", StringComparison.Ordinal)..];
        Assert.Contains("empty:hidden", region, StringComparison.Ordinal);
        Assert.Contains("role=\"alert\"", region, StringComparison.Ordinal);
        Assert.Contains("aria-live=\"polite\"", region, StringComparison.Ordinal);
        Assert.Contains("data-ui-file-refusal", region, StringComparison.Ordinal);
        Assert.True(html.IndexOf("</label>", StringComparison.Ordinal) < html.IndexOf("photos-refused\" class", StringComparison.Ordinal));
    }

    [Fact]
    public void An_upload_that_checks_nothing_draws_no_region_and_hands_no_sentence()
    {
        var upload = Ui.FileUpload.Id("photos")[Dropzone()];

        var html = upload.ToHtml();

        Assert.DoesNotContain("data-rask-refusal", html, StringComparison.Ordinal);
        Assert.DoesNotContain("photos-refused", html, StringComparison.Ordinal);
    }

    [Fact]
    public void A_hungarian_visitor_is_told_in_hungarian()
    {
        var upload = Ui.FileUpload.Id("photos").MaxSize(5.Megabytes).Accept(".pdf")[Dropzone()];

        var html = System.Net.WebUtility.HtmlDecode(KitCulture.In("hu-HU", upload.ToHtml));

        Assert.Contains("A(z) {0} túl nagy: legfeljebb 5 MB lehet.", html, StringComparison.Ordinal);
        Assert.Contains("A(z) {0} nem választható: ilyen típusú fájlt ez a mező nem fogad.", html, StringComparison.Ordinal);
        Assert.Contains("aria-label=\"Feltöltés megszakítása\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public void A_dropzone_with_a_bar_draws_a_cancel_button_that_shows_only_while_files_go()
    {
        var dropzone = Ui.FileUploadDropzone.Heading("Drop files here").Text("Up to 10 MB").WithProgress();

        var html = dropzone.ToHtml();

        Assert.Contains("data-rask-upload-cancel", html, StringComparison.Ordinal);
        Assert.Contains("type=\"button\"", html, StringComparison.Ordinal);
        Assert.Contains("aria-label=\"Cancel upload\"", html, StringComparison.Ordinal);
        Assert.Contains("invisible", html, StringComparison.Ordinal);
        Assert.Contains("in-data-loading:visible", html, StringComparison.Ordinal);
    }

    [Fact]
    public void A_dropzone_is_drawn_red_while_the_browser_says_a_choice_was_refused()
    {
        var dropzone = Ui.FileUploadDropzone.Heading("Drop files here");

        var html = dropzone.ToHtml();

        Assert.Contains("in-data-invalid:border-red-500", html, StringComparison.Ordinal);
    }

    [Fact]
    public void A_chosen_image_is_named_to_the_browser_by_its_size_time_and_name()
    {
        var item = Ui.FileItem.Heading("cat.png").Preview(new Chosen("cat.png", 2048, "image/png"));

        var html = item.ToHtml();

        Assert.Contains("data-rask-preview=\"2048:1700000000000:cat.png\"", html, StringComparison.Ordinal);
        Assert.Contains("data-slot=\"image\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain(" src=", html, StringComparison.Ordinal);
    }

    [Fact]
    public void A_chosen_file_that_is_no_image_keeps_the_icon_and_asks_for_no_preview()
    {
        var item = Ui.FileItem.Heading("invoice.pdf").Preview(new Chosen("invoice.pdf", 2048, "application/pdf"));

        var html = item.ToHtml();

        Assert.DoesNotContain("data-rask-preview", html, StringComparison.Ordinal);
        Assert.DoesNotContain("data-slot=\"image\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public void An_image_address_wins_over_a_preview()
    {
        var item = Ui.FileItem.Heading("cat.png").Image("/files/cat.png").Preview(new Chosen("cat.png", 2048, "image/png"));

        var html = item.ToHtml();

        Assert.Contains("src=\"/files/cat.png\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("data-rask-preview", html, StringComparison.Ordinal);
    }
}
