using Rask.Core.Routing;

namespace Rask.Testing;

/// <summary>
///     An <see cref="IDownloadSink" /> that records what a component staged, instead of handing it to a
///     browser.
/// </summary>
/// <remarks>
///     <para>
///         <c>Navigator.Download</c> refuses to run without a sink, and its message says <em>"If you're in
///         a unit test, register a fake."</em> — while the testing package shipped none, so everyone wrote
///         the same twenty lines. This is those twenty lines, once.
///     </para>
///     <para>
///         <see cref="Staged" /> is the assertion surface: it keeps every download in order, so a test can
///         check the file name, the content type and the bytes. <c>TryConsume</c> hands them back the way
///         a real sink does, so a component that stages and then consumes behaves the same here.
///     </para>
///     <code>
///     var downloads = new TestDownloadSink();
///     var page = RaskTest.Render(new ExportPage(new Navigator(new RouteState(), downloads)));
///     await page.ClickAsync("#export");
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
    public void Stage(string fileName, byte[] bytes, string? contentType)
    {
        ArgumentNullException.ThrowIfNull(fileName);
        ArgumentNullException.ThrowIfNull(bytes);
        Record(fileName, bytes, contentType);
    }

    /// <inheritdoc />
    public void Stage(string fileName, Stream content, string? contentType)
    {
        ArgumentNullException.ThrowIfNull(fileName);
        ArgumentNullException.ThrowIfNull(content);

        // Read it here rather than storing the stream: the component may dispose it as soon as Stage
        // returns, and a test that asserted on it later would then read from a disposed stream — a
        // failure about the harness rather than about the code under test.
        using var buffer = new MemoryStream();
        content.CopyTo(buffer);
        Record(fileName, buffer.ToArray(), contentType);
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

/// <summary>One download a component handed to <see cref="TestDownloadSink" />.</summary>
/// <param name="FileName">The name the component asked the browser to save it as.</param>
/// <param name="Bytes">The content, materialized — safe to assert on after the component disposed its source.</param>
/// <param name="ContentType">The MIME type, or <c>null</c> when the component left it to the host.</param>
public sealed record StagedDownload(string FileName, byte[] Bytes, string? ContentType)
{
    /// <summary>The content decoded as UTF-8 — the common case for a CSV, JSON or text export.</summary>
    public string Text => System.Text.Encoding.UTF8.GetString(Bytes);
}
