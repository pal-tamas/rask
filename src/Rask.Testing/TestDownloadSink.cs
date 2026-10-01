using Rask.Core.Routing;

namespace Rask.Testing;

/// <summary>
///     An <see cref="IDownloadSink" /> that records what a component staged, instead of handing it to a
///     browser.
/// </summary>
/// <remarks>
///     <para>
///         <c>Download.File</c> refuses to run without a sink, and its message says to hand the page this
///         one — the twenty lines everyone would otherwise write, once.
///     </para>
///     <para>
///         <see cref="Staged" /> is the assertion surface: it keeps every download in order, so a test can
///         check the file name, the content type and the bytes. <c>TryConsume</c> hands them back the way
///         a real sink does, so a component that stages and then consumes behaves the same here.
///     </para>
///     <code>
///     var downloads = new TestDownloadSink();
///     var page = Page.Render(new ExportPage(), TestServiceProvider.With&lt;IDownloadSink&gt;(downloads));
///     await page.On("#export").Click();
///
///     var file = Assert.Single(downloads.Staged);
///     Assert.Equal("orders.csv", file.FileName);
///     Assert.StartsWith("Id,Total", Encoding.UTF8.GetString(file.Bytes));
///     </code>
/// </remarks>
public sealed class TestDownloadSink : IDownloadSink
{
    private readonly Lock _gate = new();
    private readonly List<StagedDownload> _staged = [];
    private readonly Queue<PendingDownload> _pending = new();

    /// <summary>Every download staged so far, oldest first. Consuming one does not remove it from here.</summary>
    public IReadOnlyList<StagedDownload> Staged
    {
        get
        {
            lock (_gate)
            {
                return _staged.ToArray();
            }
        }
    }

    /// <summary>The most recent staged download, or <c>null</c> when nothing has been staged.</summary>
    public StagedDownload? Last
    {
        get
        {
            lock (_gate)
            {
                return _staged.Count == 0 ? null : _staged[^1];
            }
        }
    }

    /// <inheritdoc />
    public void Stage(string filename, byte[] bytes, string? contentType)
    {
        ArgumentNullException.ThrowIfNull(filename);
        ArgumentNullException.ThrowIfNull(bytes);
        Record(filename, bytes, contentType);
    }

    /// <inheritdoc />
    public void Stage(string filename, Stream stream, string? contentType)
    {
        ArgumentNullException.ThrowIfNull(filename);
        ArgumentNullException.ThrowIfNull(stream);

        // Read it here rather than storing the stream: the component may dispose it as soon as Stage
        // returns, and a test that asserted on it later would then read from a disposed stream — a
        // failure about the harness rather than about the code under test.
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        Record(filename, buffer.ToArray(), contentType);
    }

    /// <inheritdoc />
    public bool TryConsume(out PendingDownload? download)
    {
        lock (_gate)
        {
            if (_pending.Count == 0)
            {
                download = null;
                return false;
            }

            download = _pending.Dequeue();
            return true;
        }
    }

    private void Record(string fileName, byte[] bytes, string? contentType)
    {
        lock (_gate)
        {
            _staged.Add(new StagedDownload(fileName, bytes, contentType));
            _pending.Enqueue(new PendingDownload(fileName, contentType, Url: null, bytes));
        }
    }
}
