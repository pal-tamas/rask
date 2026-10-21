using System.ComponentModel;
using System.Runtime.CompilerServices;
using Rask.Storage.Upload;

namespace Rask.Storage;

/// <summary>
///     A <c>Save</c> still being worded: <c>await Files.Save(pdf, "invoice.pdf").Public()</c>. Nothing is
///     written until it is awaited.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public readonly struct Saving
{
    private readonly IFiles? _files;
    private readonly Func<long, CancellationToken, Stream>? _openRead;
    private readonly Stream? _content;
    private readonly string _name;
    private readonly long _size;
    private readonly bool _isPublic;
    private readonly string? _contentType;
    private readonly CancellationToken _cancellationToken;

    internal Saving(
        IFiles? files,
        Func<long, CancellationToken, Stream>? openRead,
        Stream? content,
        string name,
        long size,
        CancellationToken cancellationToken)
        : this(files, openRead, content, name ?? throw new ArgumentNullException(nameof(name)), size, false, null, cancellationToken)
    {
    }

    private Saving(
        IFiles? files,
        Func<long, CancellationToken, Stream>? openRead,
        Stream? content,
        string name,
        long size,
        bool isPublic,
        string? contentType,
        CancellationToken cancellationToken)
    {
        _files = files;
        _openRead = openRead;
        _content = content;
        _name = name;
        _size = size;
        _isPublic = isPublic;
        _contentType = contentType;
        _cancellationToken = cancellationToken;
    }

    /// <summary>
    ///     Lets anyone with the link fetch it, through <c>Files.Url(id)</c>. Without this step a file is
    ///     private: reachable only through <c>Share(id).For(…)</c> or <c>Download(id)</c>.
    /// </summary>
    public Saving Public() => new(_files, _openRead, _content, _name, _size, true, _contentType, _cancellationToken);

    /// <summary>
    ///     Records <paramref name="contentType" /> instead of the type sniffed from the bytes:
    ///     <c>await Files.Save(xml, "bevallas.xml").As("application/xml")</c>. It is still checked against
    ///     <see cref="StorageOptions.AllowedTypes" />, and served under the same rules as a sniffed type — HTML, SVG and
    ///     XML go out as <c>application/octet-stream</c> attachments.
    /// </summary>
    /// <param name="contentType">A plain media type, <c>type/subtype</c>: no parameters, spaces or line breaks.</param>
    /// <exception cref="ArgumentException"><paramref name="contentType" /> is not a plain media type.</exception>
    public Saving As(string contentType) =>
        new(_files, _openRead, _content, _name, _size, _isPublic, MediaType.Stated(contentType, nameof(contentType)),
            _cancellationToken);

    /// <summary>Runs it.</summary>
    public TaskAwaiter<StoredFile> GetAwaiter() => AsTask().GetAwaiter();

    /// <summary>Runs it, choosing whether the continuation returns to the captured context.</summary>
    public ConfiguredTaskAwaitable<StoredFile> ConfigureAwait(bool continueOnCapturedContext) =>
        AsTask().ConfigureAwait(continueOnCapturedContext);

    /// <summary>Runs it, as a <see cref="Task{TResult}" />.</summary>
    public Task<StoredFile> AsTask()
    {
        var files = _files ?? Files.Resolve();
        var token = Ambient.Or(_cancellationToken);
        if (_contentType is not { } stated)
        {
            return _openRead is { } open
                ? files.Add(open, _name, _size, _isPublic, token)
                : files.Add(_content!, _name, _isPublic, token);
        }

        if (files is not IStatedTypeFiles typed)
        {
            throw new NotSupportedException(
                $"{files.GetType().Name} cannot record a stated content type: .As(…) needs Rask's own file store or Files.Fake().");
        }

        return _openRead is { } opener
            ? typed.Add(opener, _name, _size, _isPublic, stated, token)
            : typed.Add(_content!, _name, _isPublic, stated, token);
    }
}
