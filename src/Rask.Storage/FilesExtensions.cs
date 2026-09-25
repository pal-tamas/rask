namespace Rask.Storage;

/// <summary>The steps on an injected <see cref="IFiles" />, worded as on <see cref="Files" />.</summary>
public static class FilesExtensions
{
#pragma warning disable CA2208 // CA2208 cannot see a C# 14 extension receiver
    extension(IFiles files)
    {
        /// <inheritdoc cref="Files.Save(Func{long, CancellationToken, Stream}, string, long, CancellationToken)" />
        public Saving Save(
            Func<long, CancellationToken, Stream> openRead, string name, long size, CancellationToken cancellationToken = default) =>
            new(
                files ?? throw new ArgumentNullException(nameof(files)),
                openRead ?? throw new ArgumentNullException(nameof(openRead)),
                null,
                name,
                size,
                false,
                cancellationToken);

        /// <inheritdoc cref="Files.Save(Stream, string, CancellationToken)" />
        public Saving Save(Stream content, string name, CancellationToken cancellationToken = default) =>
            new(
                files ?? throw new ArgumentNullException(nameof(files)),
                null,
                content ?? throw new ArgumentNullException(nameof(content)),
                name,
                0,
                false,
                cancellationToken);

        /// <inheritdoc cref="Files.Share(Guid, CancellationToken)" />
        public Sharing Share(Guid id, CancellationToken cancellationToken = default) =>
            new(files ?? throw new ArgumentNullException(nameof(files)), id, cancellationToken);
    }
#pragma warning restore CA2208
}
