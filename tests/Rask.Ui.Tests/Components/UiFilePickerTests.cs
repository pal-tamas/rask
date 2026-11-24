using Rask.Core;
using Rask.Core.Forms;

namespace Rask.UiTests.Components;

/// <summary>One file bound to a form's member: the upload while it is empty, the file and its remove button once picked.</summary>
public partial class UiFilePickerTests : global::Rask.Core.RaskMarkup
{
    private sealed class Registration
    {
        public PickedFile? Scan { get; set; }
    }

    [Fact]
    public void An_empty_picker_is_a_labelled_upload_with_its_dropzone()
    {
        var registration = new Registration();

        var html = Ui.FilePicker.Bind(() => registration.Scan).Label("Scan").Heading("Drop the scan here").ToHtml();

        Assert.Contains("type=\"file\"", html, StringComparison.Ordinal);
        Assert.Contains("Drop the scan here", html, StringComparison.Ordinal);
        Assert.Contains(">Scan<", html, StringComparison.Ordinal);
        Assert.DoesNotContain("data-ui-file-item", html, StringComparison.Ordinal);
    }

    [Fact]
    public void A_picked_file_is_drawn_as_its_item_with_a_button_that_removes_it()
    {
        var registration = new Registration { Scan = PickedFile.Of("scan.pdf", new byte[2048], "application/pdf") };

        var html = Ui.FilePicker.Bind(() => registration.Scan).Label("Scan").ToHtml();

        Assert.Contains("data-ui-file-item", html, StringComparison.Ordinal);
        Assert.Contains("scan.pdf", html, StringComparison.Ordinal);
        Assert.Contains("aria-label=\"Remove file: scan.pdf\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("type=\"file\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public void A_bound_field_over_a_picked_file_member_draws_a_picker()
    {
        var registration = new Registration();

        var html = Ui.Field.Bind(() => registration.Scan).Label("Scan").ToHtml();

        Assert.Contains("data-ui-file-picker", html, StringComparison.Ordinal);
        Assert.Contains("type=\"file\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_picked_file_is_read_whole_and_a_larger_one_is_refused()
    {
        var file = new TestFile("scan.pdf", new byte[300]);

        var picked = await PickedFile.Read(file, maxSize: 1_000, TestContext.Current.CancellationToken);

        Assert.Equal(300, picked.Size);
        Assert.Equal("scan.pdf", picked.Name);
        await Assert.ThrowsAsync<IOException>(() => PickedFile.Read(file, maxSize: 100, TestContext.Current.CancellationToken));
    }

    private sealed class TestFile(string name, byte[] content) : IRaskFile
    {
        public string Name { get; } = name;

        public long Size => content.LongLength;

        public string ContentType => "application/pdf";

        public DateTimeOffset LastModified => DateTimeOffset.UnixEpoch;

        public Stream OpenReadStream(long maxAllowedSize = 512 * 1024, CancellationToken cancellationToken = default) =>
            new MemoryStream(content, writable: false);
    }
}
