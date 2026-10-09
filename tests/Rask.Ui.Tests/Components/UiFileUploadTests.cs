using Rask.Core;
using Rask.Core.Forms;
using Rask.Core.Live;
using Rask.Testing;
using Page = Rask.Testing.Page;

namespace Rask.UiTests.Components;

/// <summary>
///     Flux's file upload: the native input it keeps under the dropzone, the field it draws around itself, and
///     what each part writes for the runtime and for assistive tech.
/// </summary>
/// <remarks>The look is held to fluxui.dev by <c>FileUploadParity</c>; these hold what a class string cannot say.</remarks>
public partial class UiFileUploadTests : global::Rask.Core.RaskMarkup
{
    private static Component Dropzone() =>
        Ui.FileUploadDropzone.Heading("Drop files here or click to browse").Text("JPG, PNG, GIF up to 10MB");

    [Fact]
    public void An_upload_is_a_label_around_a_real_file_input()
    {
        var upload = Ui.FileUpload[Dropzone()];

        var html = upload.ToHtml();

        Assert.StartsWith("<label class=\"relative block cursor-auto\" data-ui-file-upload", html, StringComparison.Ordinal);
        Assert.Contains("type=\"file\"", html, StringComparison.Ordinal);
        Assert.Contains("data-slot=\"receiver\"", html, StringComparison.Ordinal);
        Assert.EndsWith("</label>", html, StringComparison.Ordinal);
    }

    [Fact]
    public void The_input_comes_before_the_dropzone_so_its_states_reach_it()
    {
        var upload = Ui.FileUpload[Dropzone()];

        var html = upload.ToHtml();

        Assert.True(
            html.IndexOf("<input", StringComparison.Ordinal) < html.IndexOf("data-ui-file-upload-dropzone", StringComparison.Ordinal));
        Assert.Contains("class=\"peer sr-only", html, StringComparison.Ordinal);
    }

    [Fact]
    public void The_runtime_is_asked_to_mark_a_drag_over_the_upload_and_an_upload_in_flight()
    {
        var upload = Ui.FileUpload.OnFiles(_ => { })[Dropzone()];

        var html = upload.ToHtml();

        // data-rask-dropzone is answered with data-dragging, data-rask-loading with data-loading and --rask-progress.
        Assert.Contains("data-ui-file-upload data-rask-dropzone data-rask-loading>", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Multiple_name_and_accept_are_the_inputs_own_attributes()
    {
        var upload = Ui.FileUpload.Multiple().Name("photos").Accept("image/*")[Dropzone()];

        var html = upload.ToHtml();

        Assert.Contains(" multiple", html, StringComparison.Ordinal);
        Assert.Contains("name=\"photos\"", html, StringComparison.Ordinal);
        Assert.Contains("accept=\"image/*\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public void An_upload_with_nothing_said_writes_no_empty_attribute()
    {
        var upload = Ui.FileUpload[Dropzone()];

        var html = upload.ToHtml();

        Assert.DoesNotContain(" multiple", html, StringComparison.Ordinal);
        Assert.DoesNotContain("accept=", html, StringComparison.Ordinal);
        Assert.DoesNotContain("name=", html, StringComparison.Ordinal);
        Assert.DoesNotContain("data-ui-field", html, StringComparison.Ordinal);
    }

    [Fact]
    public void A_label_wraps_the_upload_in_a_field_and_alone_names_the_input()
    {
        var upload = Ui.FileUpload.Label("Upload files").Description("Up to ten.")[Dropzone()];

        var html = upload.ToHtml();

        Assert.Contains("data-ui-field", html, StringComparison.Ordinal);
        Assert.Contains("for=\"f-upload-files\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"f-upload-files\"", html, StringComparison.Ordinal);
        Assert.Contains("aria-labelledby=\"f-upload-files-label\"", html, StringComparison.Ordinal);
        Assert.Contains("aria-describedby=\"f-upload-files-description\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public void An_error_is_shown_under_the_upload_and_marks_the_input_invalid()
    {
        var upload = Ui.FileUpload.Label("Upload files").Error("That file is too large.")[Dropzone()];

        var html = upload.ToHtml();

        Assert.Contains("That file is too large.", html, StringComparison.Ordinal);
        Assert.Contains("aria-invalid=\"true\"", html, StringComparison.Ordinal);
        Assert.Contains("aria-describedby=\"f-upload-files-error\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public void An_error_is_shown_without_a_label_too()
    {
        var upload = Ui.FileUpload.Id("avatar").Error("Pictures only.")[Dropzone()];

        var html = upload.ToHtml();

        Assert.Contains("id=\"avatar-error\"", html, StringComparison.Ordinal);
        Assert.Contains("Pictures only.", html, StringComparison.Ordinal);
    }

    [Fact]
    public void A_disabled_upload_disables_the_input_and_takes_no_drop()
    {
        var upload = Ui.FileUpload.Disabled()[Dropzone()];

        var html = upload.ToHtml();

        Assert.Contains(" disabled", html, StringComparison.Ordinal);
        Assert.DoesNotContain("data-rask-dropzone", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Any_markup_can_stand_where_the_dropzone_does()
    {
        var upload = Ui.FileUpload.Class("w-20")[Div.Class("avatar")["Pick"]];

        var html = upload.ToHtml();

        Assert.Contains("class=\"relative block cursor-auto w-20\"", html, StringComparison.Ordinal);
        Assert.Contains("<div class=\"avatar\">Pick</div></label>", html, StringComparison.Ordinal);
        Assert.DoesNotContain("data-ui-file-upload-dropzone", html, StringComparison.Ordinal);
    }

    [Fact]
    public void A_dropzone_draws_a_cloud_a_spinner_a_heading_and_its_text()
    {
        var dropzone = Dropzone();

        var html = dropzone.ToHtml();

        Assert.StartsWith("<div class=\"flex rounded-lg border-dashed", html, StringComparison.Ordinal);
        Assert.Equal(2, Count(html, "<svg"));
        Assert.Contains("animate-spin", html, StringComparison.Ordinal);
        Assert.Contains(">Drop files here or click to browse</div>", html, StringComparison.Ordinal);
        Assert.Contains(">JPG, PNG, GIF up to 10MB</div>", html, StringComparison.Ordinal);
    }

    [Fact]
    public void A_dropzone_takes_another_icon()
    {
        var cloud = Ui.FileUploadDropzone.Heading("Drop").ToHtml();

        var photo = Ui.FileUploadDropzone.Heading("Drop").Icon(Ui.IconName.Photo).ToHtml();

        Assert.NotEqual(cloud, photo);
        Assert.Equal(2, Count(photo, "<svg"));
    }

    [Fact]
    public void An_inline_dropzone_lays_the_icon_beside_the_words()
    {
        var dropzone = Ui.FileUploadDropzone.Heading("Drop").Text("Any file").Inline();

        var html = dropzone.ToHtml();

        Assert.Contains("items-center border py-4 ps-5 pe-8", html, StringComparison.Ordinal);
        Assert.Contains("relative me-4", html, StringComparison.Ordinal);
        Assert.DoesNotContain("flex-col items-center justify-center", html, StringComparison.Ordinal);
    }

    [Fact]
    public void With_progress_the_text_gives_way_to_a_bar_that_reads_the_two_variables()
    {
        var dropzone = Ui.FileUploadDropzone.Heading("Drop").Text("Any file").WithProgress();

        var html = dropzone.ToHtml();

        Assert.Contains("w-(--ui-file-upload-progress)", html, StringComparison.Ordinal);
        Assert.Contains("after:content-(--ui-file-upload-progress-as-string)", html, StringComparison.Ordinal);
        Assert.Contains("<span class=\"in-data-loading:opacity-0\">Any file</span>", html, StringComparison.Ordinal);
    }

    [Fact]
    public void With_progress_the_icon_stays_while_files_upload()
    {
        var spinning = Ui.FileUploadDropzone.Heading("Drop").Text("Any file").ToHtml();

        var withBar = Ui.FileUploadDropzone.Heading("Drop").Text("Any file").WithProgress().ToHtml();

        Assert.Contains("in-data-loading:opacity-0", Icons(spinning), StringComparison.Ordinal);
        Assert.DoesNotContain("data-loading", Icons(withBar), StringComparison.Ordinal);
    }

    [Fact]
    public void A_dropzone_without_text_draws_no_bar()
    {
        var dropzone = Ui.FileUploadDropzone.Heading("Drop").WithProgress();

        var html = dropzone.ToHtml();

        Assert.DoesNotContain("--ui-file-upload-progress", html, StringComparison.Ordinal);
    }

    [Fact]
    public void A_dropzone_takes_the_elements_own_attributes()
    {
        var dropzone = Ui.FileUploadDropzone.Heading("Drop").Class("min-h-40").Data("loading", "").Style("--ui-file-upload-progress:40%");

        var html = dropzone.ToHtml();

        Assert.Contains("min-h-40", html, StringComparison.Ordinal);
        Assert.Contains("data-loading", html, StringComparison.Ordinal);
        Assert.Contains("data-ui-file-upload-dropzone", html, StringComparison.Ordinal);
        Assert.Contains("style=\"--ui-file-upload-progress:40%\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public void A_file_item_shows_its_name_and_its_size()
    {
        var item = Ui.FileItem.Heading("Profile_pic.jpg").Size(162400);

        var html = item.ToHtml();

        Assert.Contains("data-ui-file-item", html, StringComparison.Ordinal);
        Assert.Contains(">Profile_pic.jpg</div>", html, StringComparison.Ordinal);
        Assert.Contains(">159 KB</div>", html, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0, "0 B")]
    [InlineData(1023, "1023 B")]
    [InlineData(1024, "1 KB")]
    [InlineData(162400, "159 KB")]
    [InlineData(1572864, "1.5 MB")]
    [InlineData(10485760, "10 MB")]
    [InlineData(3221225472, "3 GB")]
    [InlineData(5497558138880, "5120 GB")]
    public void A_size_is_written_in_the_largest_unit_that_fits(long bytes, string expected)
    {
        var item = Ui.FileItem.Heading("a").Size(bytes);

        var html = item.ToHtml();

        Assert.Contains($">{expected}</div>", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Text_takes_the_place_of_the_size()
    {
        var item = Ui.FileItem.Heading("Report.pdf").Size(2048).Text("Uploaded yesterday");

        var html = item.ToHtml();

        Assert.Contains(">Uploaded yesterday</div>", html, StringComparison.Ordinal);
        Assert.DoesNotContain("2 KB", html, StringComparison.Ordinal);
    }

    [Fact]
    public void An_image_is_shown_in_place_of_the_icon()
    {
        var item = Ui.FileItem.Heading("Profile_pic.jpg").Image("/img/user.png");

        var html = item.ToHtml();

        Assert.Contains("data-slot=\"image\"", html, StringComparison.Ordinal);
        Assert.Contains("src=\"/img/user.png\"", html, StringComparison.Ordinal);
        Assert.Contains("alt=\"\"", html, StringComparison.Ordinal);
        Assert.Contains("hidden text-zinc-400", html, StringComparison.Ordinal);
    }

    [Fact]
    public void A_one_line_item_takes_the_small_icon_and_a_two_line_item_the_large_one()
    {
        var oneLine = Ui.FileItem.Heading("Brand_banner.jpg").ToHtml();

        var twoLines = Ui.FileItem.Heading("Brand_banner.jpg").Size(2048).ToHtml();

        Assert.Contains("viewBox=\"0 0 16 16\"", oneLine, StringComparison.Ordinal);
        Assert.Contains("viewBox=\"0 0 24 24\"", twoLines, StringComparison.Ordinal);
    }

    [Fact]
    public void Actions_sit_in_their_own_slot_and_only_when_given()
    {
        var bare = Ui.FileItem.Heading("a.txt").ToHtml();

        var withRemove = Ui.FileItem.Heading("a.txt").Actions(Ui.FileItemRemove).ToHtml();

        Assert.DoesNotContain("data-slot=\"actions\"", bare, StringComparison.Ordinal);
        Assert.Contains("data-slot=\"actions\"", withRemove, StringComparison.Ordinal);
        Assert.Contains("data-ui-file-item-remove", withRemove, StringComparison.Ordinal);
    }

    [Fact]
    public void An_invalid_item_is_drawn_with_a_red_border()
    {
        var item = Ui.FileItem.Heading("virus.exe").Invalid();

        var html = item.ToHtml();

        Assert.Contains("border-red-500", html, StringComparison.Ordinal);
        Assert.DoesNotContain("border-zinc-200", html, StringComparison.Ordinal);
    }

    [Fact]
    public void The_remove_button_is_a_button_named_remove_file()
    {
        var remove = Ui.FileItemRemove;

        var html = remove.ToHtml();

        Assert.StartsWith("<button", html, StringComparison.Ordinal);
        Assert.Contains("type=\"button\"", html, StringComparison.Ordinal);
        Assert.Contains("aria-label=\"Remove file\"", html, StringComparison.Ordinal);
        Assert.Contains("data-ui-button", html, StringComparison.Ordinal);
    }

    [Fact]
    public void The_remove_button_takes_a_name_of_its_own()
    {
        var remove = Ui.FileItemRemove.AriaLabel("Remove file: Profile_pic.jpg");

        var html = remove.ToHtml();

        Assert.Contains("aria-label=\"Remove file: Profile_pic.jpg\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Chosen_files_reach_the_page_and_it_draws_an_item_for_each()
    {
        var files = new TestFileBackend();
        files.Add("march.pdf", "one");
        files.Add("april.jpg", "three");
        var kept = new List<(string Name, long Size)>();
        var page = Page.Render(() => Uploader(kept), TestServiceProvider.With<IBrowserFileBackend>(files));

        await page.On("#receipts").Files(files);

        Assert.Equal(["march.pdf3 B", "april.jpg5 B"], page.FindAll("[data-slot=\"content\"]").Select(n => n.TextContent));
    }

    [Fact]
    public async Task The_remove_button_runs_the_pages_handler_and_the_item_goes()
    {
        var kept = new List<(string Name, long Size)> { ("march.pdf", 3), ("april.jpg", 5) };
        var page = Page.Render(() => Uploader(kept));

        await page.On("[aria-label=\"Remove file: march.pdf\"]").Click();

        Assert.Equal([("april.jpg", 5L)], kept);
        Assert.Single(page.FindAll("[data-ui-file-item]"));
    }

    // What a page writes: the upload, and under it one item per file it kept.
    private static Component Uploader(List<(string Name, long Size)> kept) =>
        Div[
            Ui.FileUpload.Id("receipts").Multiple().OnFiles(files => kept.AddRange(files.Select(f => (f.Name, f.Size))))[Dropzone()],
            kept.Select(file =>
                Ui.FileItem.Key(file.Name).Heading(file.Name).Size(file.Size)
                    .Actions(Ui.FileItemRemove.AriaLabel("Remove file: " + file.Name).OnClick(() => kept.Remove(file))))
        ];

    private static int Count(string haystack, string needle) =>
        haystack.Split(needle, StringSplitOptions.None).Length - 1;

    // The icon column: everything before the words.
    private static string Icons(string dropzone) =>
        dropzone[..dropzone.IndexOf("flex flex-col", StringComparison.Ordinal)];
}
