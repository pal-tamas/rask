using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Rask.Storage;

/// <summary>
///     The app's stored files, with nothing injected — from a handler, a render, a request, a job:
/// </summary>
/// <remarks>
///     <code>
///     var saved = await Files.Save(file.OpenReadStream, file.Name, file.Size);
///     var avatar = await Files.Save(picked.OpenReadStream, picked.Name, picked.Size).Public();
///     var link = await Files.Share(invoice.PdfId).For(15.Minutes);
///     await Files.Delete(saved.Id);
///     </code>
///     <para>
///         Each call reaches the <see cref="IFiles" /> of the work it runs in and is cancelled with that work.
///         Outside any — a hosted service, a timer started at boot — it throws; inject <see cref="IFiles" />
///         there instead.
///     </para>
/// </remarks>
public static class Files
{
    /// <summary>
    ///     Stores an upload and records it. Pass the opener as a method group — <c>file.OpenReadStream</c> —
    ///     and Rask calls it once, with the storage limit.
    /// </summary>
    /// <param name="openRead">Opens the content, given the largest size to accept.</param>
    /// <param name="name">The display name, as the user's file was called.</param>
    /// <param name="size">The declared size in bytes, checked before anything is read.</param>
    /// <param name="cancellationToken">Cancels the save.</param>
    public static Saving Save(
        Func<long, CancellationToken, Stream> openRead, string name, long size, CancellationToken cancellationToken = default) =>
        new(null, openRead ?? throw new ArgumentNullException(nameof(openRead)), null, name, size, false, cancellationToken);

    /// <summary>Stores <paramref name="content" /> under the display name <paramref name="name" />.</summary>
    /// <param name="content">The bytes to store. Read to the end, not disposed.</param>
    /// <param name="name">The display name.</param>
    /// <param name="cancellationToken">Cancels the save.</param>
    public static Saving Save(Stream content, string name, CancellationToken cancellationToken = default) =>
        new(null, null, content ?? throw new ArgumentNullException(nameof(content)), name, 0, false, cancellationToken);

    /// <summary>The row for <paramref name="id" />, or <c>null</c> when there is none.</summary>
    public static Task<StoredFile?> Get(Guid id, CancellationToken cancellationToken = default) =>
        Resolve().Get(id, Ambient.Or(cancellationToken));

    /// <summary>Opens the file's bytes, or <c>null</c> when there is no such file. The caller owns the stream.</summary>
    public static Task<Stream?> OpenRead(Guid id, CancellationToken cancellationToken = default) =>
        Resolve().OpenRead(id, Ambient.Or(cancellationToken));

    /// <summary>Removes the file. <c>false</c> when there was no such file.</summary>
    public static Task<bool> Delete(Guid id, CancellationToken cancellationToken = default) =>
        Resolve().Delete(id, Ambient.Or(cancellationToken));

    /// <summary>The public URL of a file saved with <c>.Public()</c>. Reads nothing, so it is free in a render.</summary>
    public static string Url(Guid id) => Resolve().Url(id);

    /// <summary>A link to any file that stops working after the <c>For</c> step: <c>Share(id).For(15.Minutes)</c>.</summary>
    public static Sharing Share(Guid id, CancellationToken cancellationToken = default) =>
        new(null, id, cancellationToken);

    /// <summary>A response that streams the file, for an endpoint that has checked the caller may see it.</summary>
    public static IResult Download(Guid id) => Resolve().Download(id);

    /// <summary>What <c>Files.Fake()</c> put in the way of the real store, for this test's flow alone.</summary>
    internal static readonly AsyncLocal<IFiles?> Faked = new();

    internal static IFiles Resolve()
    {
        if (Faked.Value is { } fake)
        {
            return fake;
        }

        var services = Ambient.Services
            ?? throw new InvalidOperationException(
                "Files was called outside any work in progress — a handler, a render, a request or a job — so "
                + "there is no app to reach. Inject IFiles in the constructor there instead.");

        return services.GetService<IFiles>()
            ?? throw new InvalidOperationException(
                "Files needs Rask.Storage registered: call builder.Services.AddRaskStorage<AppDbContext>().");
    }
}

/// <summary>The steps on an injected <see cref="IFiles" />, worded as on <see cref="Files" />.</summary>
public static class FilesExtensions
{
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
}
