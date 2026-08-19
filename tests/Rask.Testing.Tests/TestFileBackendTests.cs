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
    public async Task AFilesEvent_ReachesTheHandler_WithRealBytes()
    {
        var files = new TestFileBackend();
        var picked = files.Add("notes.txt", "hello world", "text/plain");

        var page = RaskTest.Render(UploadProbe, TestServiceProvider.With<IBrowserFileBackend>(files));
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
    public async Task WithNoBackendRegistered_TheHandlerGetsAnEmptyList()
    {
        var files = new TestFileBackend();
        var picked = files.Add("notes.txt", "hello world", "text/plain");

        // Capture the report and assert it: the framework tells you about this case (RaskDiagnostics, added
        // with the host-parity fix) and nothing else pins that the warning fires at all. Capturing also keeps
        // this process-global diagnostic out of a parallel test's window — belt to #750's braces, which fixed
        // the real bug by making the wait there look for its own diagnostic rather than the first to arrive.
        using var diagnostics = CapturingDiagnostics.Install();

        var page = RaskTest.Render(UploadProbe);
        await page.On("#picker").FilesAsync(picked);

        Assert.True(page.Instance.Fired, "the handler still runs");
        Assert.Empty(page.Instance.Received);
        Assert.Contains(diagnostics.Captured, e =>
            e.Category == "Rask.Forms" && e.Message.Contains("no IBrowserFileBackend is registered",
                StringComparison.Ordinal));
    }

    [Fact]
    public async Task EveryStagedFile_IsPickedWhenThePickerIsGivenTheBackend()
    {
        var files = new TestFileBackend();
        files.Add("a.txt", "one");
        files.Add("b.txt", "two");

        var page = RaskTest.Render(UploadProbe, TestServiceProvider.With<IBrowserFileBackend>(files));
        await page.On("#picker").FilesAsync(files);

        Assert.Equal(["a.txt", "b.txt"], page.Instance.Received.Select(f => f.Name));
    }

    [Fact]
    public async Task TheFrameworkReleasesTheFiles_AfterTheHandlerReturns()
    {
        // The browser hosts drop their client-side references here and the server frees its upload slot, so a
        // component that holds a RaskFile past the handler is holding something already gone.
        var files = new TestFileBackend();
        var page = RaskTest.Render(UploadProbe, TestServiceProvider.With<IBrowserFileBackend>(files));

        await page.On("#picker").FilesAsync(files.Add("notes.txt", "hi"));

        Assert.Equal(["notes.txt"], files.Released);
    }

    [Fact]
    public void Add_DefaultsAreDeterministic_SoATestIsNotTimeDependent()
    {
        var file = new TestFileBackend().Add("blob.bin", new byte[] { 1, 2, 3 });

        Assert.Equal("application/octet-stream", file.ContentType);
        Assert.Equal(DateTimeOffset.UnixEpoch, file.LastModified);
        Assert.Equal(3, file.Size);
    }

    [Fact]
    public void Add_TextOverload_EncodesUtf8_AndDefaultsToTextPlain()
    {
        var file = new TestFileBackend().Add("notes.txt", "héllo");

        Assert.Equal("text/plain", file.ContentType);
        Assert.Equal(Encoding.UTF8.GetBytes("héllo"), file.Bytes);
    }

    [Fact]
    public void OpenReadStream_EnforcesMaxAllowedSize_LikeTheRealBackends()
    {
        // So a component that forgot to raise the limit for a large upload fails in a unit test rather than
        // on a real file.
        var file = new TestFileBackend().Add("big.bin", new byte[1024]);

        Assert.Throws<IOException>(() => file.OpenReadStream(512));
    }

    [Fact]
    public void Create_WithAnUnstagedRef_SaysSo()
    {
        var backend = new TestFileBackend();
        var meta = System.Text.Json.JsonDocument.Parse("""{"ref":"nope","name":"x.txt","size":1}""").RootElement;

        var ex = Assert.Throws<InvalidOperationException>(() => backend.Create(meta));

        Assert.Contains("nope", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FormPayload_DeliversFilesUnderTheirFieldName()
    {
        var files = new TestFileBackend();
        var page = RaskTest.Render(UploadFormProbe, TestServiceProvider.With<IBrowserFileBackend>(files));

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

    private void OnSubmit(FormData form) => Received = form.Files("attachment");

    protected override Component? Render() =>
        Form.Model(_model).Id("form").OnSubmit(OnSubmit)[
            Input.Value<string>(null).Type(InputType.File).Name("attachment")
        ];

    // Form is Form<TModel>; the model only exists to open the chain.
    internal sealed class Attachment;
}
