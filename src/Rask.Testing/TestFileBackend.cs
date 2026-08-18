using System.Text;
using System.Text.Json;
using Rask.Core.Forms;

namespace Rask.Testing;

/// <summary>
///     An <see cref="IBrowserFileBackend" /> that serves files a test staged in memory, instead of reading
///     them back from a browser.
/// </summary>
/// <remarks>
///     <para>
///         Without one registered, an <c>OnFiles</c> handler cannot be unit-tested <b>at all</b>, and — worse
///         — it looks like it can. <c>FileListReader</c> resolves the backend from the container and hands
///         the handler an <b>empty list</b> when there is none, so a test that renders a file input and
///         raises its event exercises the empty branch and passes on whatever the handler does with nothing.
///         That is the same silent-empty failure the native host shipped with, reproduced in every test.
///     </para>
///     <para>
///         Stage the files, register the backend, raise the event:
///     </para>
///     <code>
///     var files = new TestFileBackend();
///     files.Add("notes.txt", "hello", "text/plain");
///
///     var page = RaskTest.Render(new UploadPage(), TestServiceProvider.With&lt;IBrowserFileBackend&gt;(files));
///     await page.On("#picker").FilesAsync(files);
///
///     Assert.Equal("notes.txt", page.TextOf("[data-testid=name]"));
///     </code>
///     <para>
///         <see cref="Released" /> records what the framework handed back after the handler returned, so a
///         test can assert the release half of the contract too.
///     </para>
/// </remarks>
public sealed class TestFileBackend : IBrowserFileBackend
{
    private readonly Lock _gate = new();
    private readonly List<string> _released = [];
    private readonly List<TestFile> _staged = [];

    /// <summary>Every file staged so far, in the order it was added.</summary>
    public IReadOnlyList<TestFile> Staged
    {
        get
        {
            lock (_gate)
            {
                return _staged.ToArray();
            }
        }
    }

    /// <summary>
    ///     The names of the files the framework released after a handler returned, oldest first. The browser
    ///     hosts drop their references at this point and the server host frees its upload slot, so a
    ///     component that holds a <see cref="RaskFile" /> past the handler is holding something already gone.
    /// </summary>
    public IReadOnlyList<string> Released
    {
        get
        {
            lock (_gate)
            {
                return _released.ToArray();
            }
        }
    }

    /// <inheritdoc />
    public RaskFile Create(JsonElement metadata)
    {
        var reference = metadata.TryGetProperty("ref", out var r) && r.ValueKind == JsonValueKind.String
            ? r.GetString()
            : null;

        lock (_gate)
        {
            foreach (var file in _staged)
            {
                if (file.Ref == reference)
                {
                    return file;
                }
            }
        }

        // A ref the test never staged is a mistake in the test, not a condition to model: the real backends
        // are handed refs their own client minted moments earlier. Saying so beats returning an empty file
        // and letting the assertion fail somewhere else.
        throw new InvalidOperationException(
            $"No file staged under ref '{reference}'. Stage it with TestFileBackend.Add(...) and build the "
            + "event payload from what Add returned (or from the backend's Payload()), rather than writing "
            + "the JSON by hand.");
    }

    /// <inheritdoc />
    public void Release(IEnumerable<RaskFile> files)
    {
        ArgumentNullException.ThrowIfNull(files);
        lock (_gate)
        {
            foreach (var file in files)
            {
                _released.Add(file.Name);
            }
        }
    }

    /// <summary>Stages <paramref name="bytes" /> as a pickable file and returns the handle to build a payload from.</summary>
    /// <param name="name">The file name the handler will see.</param>
    /// <param name="bytes">The content the handler will read from <c>OpenReadStream</c>.</param>
    /// <param name="contentType">MIME type; defaults to <c>application/octet-stream</c>.</param>
    /// <param name="lastModified">Defaults to the Unix epoch, so a test is not time-dependent by accident.</param>
    public TestFile Add(string name, byte[] bytes, string? contentType = null,
        DateTimeOffset? lastModified = null)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(bytes);

        lock (_gate)
        {
            var file = new TestFile(
                "test-file-" + _staged.Count.ToString(System.Globalization.CultureInfo.InvariantCulture),
                name,
                bytes,
                contentType ?? "application/octet-stream",
                lastModified ?? DateTimeOffset.UnixEpoch);
            _staged.Add(file);
            return file;
        }
    }

    /// <summary>Stages <paramref name="text" /> as UTF-8 — the common case for a CSV, JSON or text upload.</summary>
    /// <param name="name">The file name the handler will see.</param>
    /// <param name="text">The content, encoded as UTF-8.</param>
    /// <param name="contentType">MIME type; defaults to <c>text/plain</c> for this overload.</param>
    public TestFile Add(string name, string text, string? contentType = null)
    {
        ArgumentNullException.ThrowIfNull(text);
        return Add(name, Encoding.UTF8.GetBytes(text), contentType ?? "text/plain");
    }

    /// <summary>
    ///     The payload for a file input's <c>files</c> event, carrying every staged file. Pass
    ///     <paramref name="files" /> to send only some of them — e.g. when one test stages files for two
    ///     different inputs.
    /// </summary>
    public string Payload(params TestFile[] files) =>
        PayloadFor(files.Length > 0 ? files : Staged);

    /// <summary>
    ///     The <c>files</c>-event payload for <paramref name="files" />, without going through a backend
    ///     instance — a <see cref="TestFile" /> carries its own metadata. This is what
    ///     <c>page.On("#picker").FilesAsync(file)</c> uses.
    /// </summary>
    public static string PayloadFor(params TestFile[] files)
    {
        ArgumentNullException.ThrowIfNull(files);
        return PayloadFor((IReadOnlyList<TestFile>)files);
    }

    private static string PayloadFor(IReadOnlyList<TestFile> files) =>
        "{\"files\":" + MetadataArray(files) + "}";

    /// <summary>
    ///     The payload for a <c>submit</c> whose form contains a file input named <paramref name="fieldName" />
    ///     — the shape <c>FormData.Files(fieldName)</c> reads. Merge extra text fields yourself if the form
    ///     has them; this covers the file half.
    /// </summary>
    public string FormPayload(string fieldName, params TestFile[] files)
    {
        ArgumentNullException.ThrowIfNull(fieldName);
        return "{\"form\":{\"__files\":{" + JsonSerializer.Serialize(fieldName) + ":"
               + MetadataArray(files.Length > 0 ? files : Staged) + "}}}";
    }

    private static string MetadataArray(IReadOnlyList<TestFile> files)
    {
        var sb = new StringBuilder("[");
        for (var i = 0; i < files.Count; i++)
        {
            if (i > 0)
            {
                sb.Append(',');
            }

            sb.Append(files[i].Metadata);
        }

        return sb.Append(']').ToString();
    }
}

/// <summary>
///     One file staged in a <see cref="TestFileBackend" />. It <em>is</em> the <see cref="RaskFile" /> the
///     handler receives, so a test can compare identity as well as content.
/// </summary>
public sealed class TestFile : RaskFile
{
    internal TestFile(string reference, string name, byte[] bytes, string contentType,
        DateTimeOffset lastModified)
    {
        Ref = reference;
        Name = name;
        Bytes = bytes;
        ContentType = contentType;
        LastModified = lastModified;
    }

    /// <summary>The handle the event payload refers to this file by.</summary>
    public string Ref { get; }

    /// <summary>The staged content.</summary>
    public byte[] Bytes { get; }

    /// <inheritdoc />
    public override string Name { get; }

    /// <inheritdoc />
    public override long Size => Bytes.Length;

    /// <inheritdoc />
    public override string ContentType { get; }

    /// <inheritdoc />
    public override DateTimeOffset LastModified { get; }

    /// <summary>This file's entry in an event payload — the same metadata a real client would send.</summary>
    public string Metadata =>
        "{\"ref\":" + JsonSerializer.Serialize(Ref)
                    + ",\"name\":" + JsonSerializer.Serialize(Name)
                    + ",\"size\":" + Size.ToString(System.Globalization.CultureInfo.InvariantCulture)
                    + ",\"type\":" + JsonSerializer.Serialize(ContentType)
                    + ",\"lastModified\":"
                    + LastModified.ToUnixTimeMilliseconds().ToString(System.Globalization.CultureInfo.InvariantCulture)
                    + "}";

    /// <inheritdoc />
    public override Stream OpenReadStream(long maxAllowedSize = 512 * 1024,
        CancellationToken cancellationToken = default)
    {
        // Enforced here as the real backends do, so a test catches a component that forgot to raise the
        // limit for a large upload instead of only finding out on a real file.
        if (Size > maxAllowedSize)
        {
            throw new IOException($"File '{Name}' is {Size} bytes, exceeds maxAllowedSize of {maxAllowedSize}.");
        }

        return new MemoryStream(Bytes, writable: false);
    }
}
