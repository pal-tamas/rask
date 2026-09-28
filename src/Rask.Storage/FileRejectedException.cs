using System.Globalization;

namespace Rask.Storage;

/// <summary>
/// A file <c>Files.Save</c>
/// refused. Nothing was stored. The message is safe to log; it never repeats the uploaded file name.
/// </summary>
public sealed class FileRejectedException : InvalidOperationException
{
    /// <summary>A refusal with no detail. Rask throws the specific ones; this exists for tests and serializers.</summary>
    public FileRejectedException()
    {
    }

    /// <summary>A refusal described by <paramref name="message"/>.</summary>
    public FileRejectedException(string message)
        : base(message)
    {
    }

    /// <summary>A refusal described by <paramref name="message"/>, caused by <paramref name="innerException"/>.</summary>
    public FileRejectedException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    private FileRejectedException(FileRejection reason, string message, long? size, long? limit, string? contentType)
        : base(message)
    {
        Reason = reason;
        Size = size;
        Limit = limit;
        ContentType = contentType;
    }

    /// <summary>Why the file was refused.</summary>
    public FileRejection Reason { get; }

    /// <summary>For <see cref="FileRejection.TooLarge"/>, how many bytes had been read when the limit was passed.</summary>
    public long? Size { get; }

    /// <summary>For <see cref="FileRejection.TooLarge"/>, the limit in bytes.</summary>
    public long? Limit { get; }

    /// <summary>For <see cref="FileRejection.TypeNotAllowed"/>, the sniffed media type.</summary>
    public string? ContentType { get; }

    internal static FileRejectedException TooLarge(long size, long limit) => new(
        FileRejection.TooLarge,
        string.Create(CultureInfo.InvariantCulture,
            $"The file is at least {size} bytes; the limit is {limit}. Raise it with "
            + $"builder.Services.AddRaskStorage<AppDbContext>(o => o.MaxFileSize = 100 * 1024 * 1024), or the "
            + $"Rask__Storage__MaxFileSize setting."),
        size, limit, null);

    internal static FileRejectedException TypeNotAllowed(string contentType) => new(
        FileRejection.TypeNotAllowed,
        $"The file's content is {contentType}, which StorageOptions.AllowedTypes does not include. Allow it with "
        + $"builder.Services.AddRaskStorage<AppDbContext>(o => o.AllowedTypes.Add(\"{contentType}\")).",
        null, null, contentType);
}
