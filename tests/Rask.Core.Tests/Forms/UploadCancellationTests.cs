using System.Text.Json;
using Rask.Core.Forms;

#pragma warning disable RASK014 // test-defined Component subclasses have no generated factories

namespace Rask.Core.Tests.Forms;

// A backend like the browser host's: each upload's cancellation, which the test pulls as the reader would.
internal sealed class CancellableBackend : IBrowserFileBackend, ICancellableFiles
{
    private readonly TestFileBackend _files = new();

    internal CancellationTokenSource Reader { get; } = new();

    internal TestFile Add(string name) => _files.Add(name, new byte[16], "image/png");

    public IRaskFile Create(JsonElement metadata) => _files.Create(metadata);

    public void Release(IEnumerable<IRaskFile> files) => _files.Release(files);

    public CancellationToken CancellationOf(IReadOnlyList<IRaskFile> files) => Reader.Token;
}

// A page whose files handler is called off in the middle: the reader's cancel lands while it reads.
internal sealed partial class UploadsPage : Component
{
    internal readonly CancellableBackend Backend = new();
    internal string Said = "nothing";
    internal CancellationToken Seen;

    protected override Component? Render() =>
        Div[
            Input.Value<string>(null).Type(InputType.File).Id("photos").OnFiles(Receive),
            P.Id("said")[Said]
        ];

    private async Task Receive(IReadOnlyList<IRaskFile> files)
    {
        Seen = Ambient.CancellationToken;
        Said = "reading";
        await Backend.Reader.CancelAsync();
        Ambient.CancellationToken.ThrowIfCancellationRequested();
        Said = "saved " + files.Count;
    }
}

// What Ui.FileUpload is to a page: a component of its own around the input, handing the files on to the page's callback.
public sealed partial class WrappedPicker : Component
{
    public Callback<IReadOnlyList<IRaskFile>> OnFiles { get; set; }

    protected override Component? Render() => Input.Value<string>(null).Type(InputType.File).Id("wrapped").OnFiles(OnFiles);
}

// A page whose handler is reached through that component: the callback runs under the page's lifetime, not the picker's.
internal sealed partial class WrappedUploadsPage : Component
{
    internal readonly CancellableBackend Backend = new();
    internal string Said = "nothing";

    protected override Component? Render() => Div[WrappedPicker.OnFiles(Receive)];

    private async Task Receive(IReadOnlyList<IRaskFile> files)
    {
        Said = "reading";
        await Backend.Reader.CancelAsync();
        Ambient.CancellationToken.ThrowIfCancellationRequested();
        Said = "saved " + files.Count;
    }
}

/// <summary>
///     A files handler on a host whose reader can call the upload off (the browser host): it runs under that
///     cancellation as its ambient one, and a cancel ends it quietly — no fault, no report.
/// </summary>
public partial class UploadCancellationTests : global::Rask.Core.RaskMarkup
{
    [Fact]
    public async Task A_handler_the_reader_calls_off_stops_where_it_was_told_and_nothing_fails()
    {
        var uploads = new UploadsPage();
        var cat = uploads.Backend.Add("cat.png");
        var page = Page.Render(uploads, TestServiceProvider.With<IBrowserFileBackend>(uploads.Backend));

        await page.On("#photos").Files(cat);

        Assert.Equal("reading", uploads.Said);
        Assert.True(uploads.Seen.IsCancellationRequested);
        Assert.Equal("reading", page.TextOf("#said"));
    }

    [Fact]
    public async Task A_handler_reached_through_a_component_around_the_input_is_called_off_all_the_same()
    {
        var uploads = new WrappedUploadsPage();
        var cat = uploads.Backend.Add("cat.png");
        var page = Page.Render(uploads, TestServiceProvider.With<IBrowserFileBackend>(uploads.Backend));

        await page.On("#wrapped").Files(cat);

        Assert.Equal("reading", uploads.Said);
    }

    [Fact]
    public async Task A_host_that_cannot_call_an_upload_off_runs_the_handler_as_before()
    {
        var files = new TestFileBackend();
        var cat = files.Add("cat.png", new byte[4], "image/png");
        var received = 0;
        var page = Page.Render(
            () => Input.Value<string>(null).Type(InputType.File).Id("photos").OnFiles(list => received = list.Count),
            TestServiceProvider.With<IBrowserFileBackend>(files));

        await page.On("#photos").Files(cat);

        Assert.Equal(1, received);
    }
}
