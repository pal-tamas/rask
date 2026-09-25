using System.ComponentModel;
using System.Runtime.CompilerServices;

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
    private readonly CancellationToken _cancellationToken;

    internal Saving(
        IFiles? files,
        Func<long, CancellationToken, Stream>? openRead,
        Stream? content,
        string name,
        long size,
        bool isPublic,
        CancellationToken cancellationToken)
    {
        _files = files;
        _openRead = openRead;
        _content = content;
        _name = name ?? throw new ArgumentNullException(nameof(name));
        _size = size;
        _isPublic = isPublic;
        _cancellationToken = cancellationToken;
    }

    /// <summary>
    ///     Lets anyone with the link fetch it, through <c>Files.Url(id)</c>. Without this step a file is
    ///     private: reachable only through <c>Share(id).For(…)</c> or <c>Download(id)</c>.
    /// </summary>
    public Saving Public() => new(_files, _openRead, _content, _name, _size, true, _cancellationToken);

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
        return _openRead is { } open
            ? files.Add(open, _name, _size, _isPublic, token)
            : files.Add(_content!, _name, _isPublic, token);
    }
}
