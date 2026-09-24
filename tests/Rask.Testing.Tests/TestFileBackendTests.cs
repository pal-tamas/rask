using System.Text;
using Rask.Core.Forms;
using Rask.Core.Live;

namespace Rask.Testing.Tests;

// An OnFiles handler could not be unit-tested before this: FileListReader resolves IBrowserFileBackend from
// the container and hands the handler an EMPTY list when there is none, so a test that rendered a file input
// and raised its event exercised the empty branch and passed on whatever the handler did with nothing. The
// end-to-end facts below are the ones that matter — the rest guard the seams they lean on.
[Collection("rask-global-diagnostics")]
public partial class TestFileBackendTests : global::Rask.Core.RaskMarkup
{
    [Fact]
    public async Task A_files_event_reaches_the_handler_with_real_bytes()
    {
        var files = new TestFileBackend();
        var picked = files.Add("notes.txt", "hello world", "text/plain");
        var page = Page.Render(UploadProbe, TestServiceProvider.With<IBrowserFileBackend>(files));

        await page.On("#picker").FilesAsync(picked);

        var received = Assert.Single(page.Instance.Received);
        Assert.Equal("notes.txt", received.Name);
        Assert.Equal("text/plain", received.ContentType);
        Assert.Equal(11, received.Size);
        Assert.Equal("hello world", page.Instance.ReadBack);
    }

    // The failure this whole type exists to end: without a backend the handler still fires, with nothing in
    // it. Pinning it means a future refactor that quietly re-breaks the resolution shows up here.
    [Fact]
    public async Task With_no_backend_registered_the_handler_gets_an_empty_list()
    {
        var files = new TestFileBackend();
        var picked = files.Add("notes.txt", "hello world", "text/plain");
        // Capture the report and assert it: the framework tells you about this case (RaskDiagnostics, added
        // with the host-parity fix) and nothing else pins that the warning fires at all. Capturing also keeps
        // this process-global diagnostic out of a parallel test's window — belt to #750's braces, which fixed
        // the real bug by making the wait there look for its own diagnostic rather than the first to arrive.
        using var diagnostics = CapturingDiagnostics.Install();
        var page = Page.Render(UploadProbe);

        await page.On("#picker").FilesAsync(picked);

        Assert.True(page.Instance.Fired, "the handler still runs");
        Assert.Empty(page.Instance.Received);
        Assert.Contains(diagnostics.Captured, e =>
            e.Category == "Rask.Forms" && e.Message.Contains("no IBrowserFileBackend is registered",
                StringComparison.Ordinal));
    }

    [Fact]
    public async Task Every_staged_file_is_picked_when_the_picker_is_given_the_backend()
    {
        var files = new TestFileBackend();
        files.Add("a.txt", "one");
        files.Add("b.txt", "two");
        var page = Page.Render(UploadProbe, TestServiceProvider.With<IBrowserFileBackend>(files));

        await page.On("#picker").FilesAsync(files);

        Assert.Equal(["a.txt", "b.txt"], page.Instance.Received.Select(f => f.Name));
    }

    [Fact]
    public async Task The_framework_releases_the_files_after_the_handler_returns()
    {
        // The browser hosts drop their client-side references here and the server frees its upload slot, so a
        // component that holds a RaskFile past the handler is holding something already gone.
        var files = new TestFileBackend();
        var page = Page.Render(UploadProbe, TestServiceProvider.With<IBrowserFileBackend>(files));

        await page.On("#picker").FilesAsync(files.Add("notes.txt", "hi"));

        Assert.Equal(["notes.txt"], files.Released);
    }

    [Fact]
    public void Added_file_defaults_are_deterministic_so_a_test_is_not_time_dependent()
    {
        var file = new TestFileBackend().Add("blob.bin", new byte[] { 1, 2, 3 });

        Assert.Equal("application/octet-stream", file.ContentType);
        Assert.Equal(DateTimeOffset.UnixEpoch, file.LastModified);
        Assert.Equal(3, file.Size);
    }

    [Fact]
    public void Adding_text_encodes_utf8_and_defaults_to_text_plain()
    {
        var file = new TestFileBackend().Add("notes.txt", "héllo");

        Assert.Equal("text/plain", file.ContentType);
        Assert.Equal(Encoding.UTF8.GetBytes("héllo"), file.Bytes);
    }

    [Fact]
    public void OpenReadStream_enforces_the_max_allowed_size_like_the_real_backends()
    {
        // So a component that forgot to raise the limit for a large upload fails in a unit test rather than
        // on a real file.
        var file = new TestFileBackend().Add("big.bin", new byte[1024]);

        Assert.Throws<IOException>(() => file.OpenReadStream(512));
    }

    [Fact]
    public void Creating_a_file_from_an_unstaged_ref_says_so()
    {
        var backend = new TestFileBackend();
        var meta = System.Text.Json.JsonDocument.Parse("""{"ref":"nope","name":"x.txt","size":1}""").RootElement;

        var ex = Assert.Throws<InvalidOperationException>(() => backend.Create(meta));

        Assert.Contains("nope", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_form_payload_delivers_files_under_their_field_name()
    {
        var files = new TestFileBackend();
        var page = Page.Render(UploadFormProbe, TestServiceProvider.With<IBrowserFileBackend>(files));

        await page.On("#form").SubmitAsync(files.FormPayload("attachment", files.Add("cv.pdf", "x")));

        Assert.Equal(["cv.pdf"], page.Instance.Received.Select(f => f.Name));
    }
}

internal sealed partial class UploadProbe : Component
{
    public bool Fired { get; private set; }
    public IReadOnlyList<RaskFile> Received { get; private set; } = [];
    public string? ReadBack { get; private set; }

    private void OnFiles(IReadOnlyList<RaskFile> files)
    {
        Fired = true;
        Received = files;
        if (files.Count == 0)
        {
            return;
        }

        // Read through the real RaskFile API, so the test proves the stream works and not just the metadata.
        using var reader = new StreamReader(files[0].OpenReadStream());
        ReadBack = reader.ReadToEnd();
    }

    protected override Component? Render() =>
        Input.Value<string>(null).Id("picker").Type(InputType.File).OnFiles(OnFiles);
}

internal sealed partial class UploadFormProbe : Component
{
    private readonly Attachment _model = new();

    public IReadOnlyList<RaskFile> Received { get; private set; } = [];

    private void OnAnySubmit(FormData form) => Received = form.Files("attachment");

    protected override Component? Render() =>
        Form.Model(_model).Id("form").OnAnySubmit(OnAnySubmit)[
            Input.Value<string>(null).Type(InputType.File).Name("attachment")
        ];

    // Form is Form<TModel>; the model only exists to open the chain.
    internal sealed class Attachment;
}
