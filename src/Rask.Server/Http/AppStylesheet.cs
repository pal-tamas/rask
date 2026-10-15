using System.Reflection;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.FileProviders;

namespace Rask.Server.Http;

/// <summary>
///     The app's own compiled stylesheet (Rask.Tailwind's <c>wwwroot/css/app.css</c>), served the way Rask serves its
///     runtime: compressed, revalidated by its ETag, and kept for a year under <c>?v={hash}</c>.
/// </summary>
/// <remarks>
///     <para>
///         Left to the app's static files, a Tailwind + Rask.Ui sheet (650–750 KB) crossed the wire uncompressed and
///         without a cache header on every page load. This answers its address ahead of everything the app put in its
///         pipeline (see <see cref="Startup" />), so a host that calls <c>UseStaticFiles()</c> itself, or writes its
///         own <c>&lt;link&gt;</c>, gets it too, with no line of its own.
///     </para>
///     <para>
///         Read from the web root and hashed when first asked for, then again only when the file's time or length
///         moves, looked at no more than once a second — a rebuild under <c>rask dev</c> is picked up; a render is not
///         slowed by a file system call.
///     </para>
/// </remarks>
internal sealed class AppStylesheet
{
    private const string ContentType = "text/css; charset=utf-8";

    private readonly IFileProvider _files;
    private readonly string _path;
    private readonly long _lookAgainMs;
    private readonly Lock _gate = new();
    private Snapshot? _current;
    private long _lookedAt;
    private bool _looked;

    /// <summary>The sheet at <paramref name="path" /> in <paramref name="files" />.</summary>
    /// <param name="files">The web root.</param>
    /// <param name="path">The sheet's path in it: <c>css/app.css</c>.</param>
    /// <param name="lookAgain">How long a look at the file holds; a second unless a test says otherwise.</param>
    public AppStylesheet(IFileProvider files, string path, TimeSpan? lookAgain = null)
    {
        _files = files;
        _path = path.TrimStart('/');
        _lookAgainMs = (long)(lookAgain ?? TimeSpan.FromSeconds(1)).TotalMilliseconds;
        RequestPath = new PathString('/' + _path);
    }

    /// <summary>Where the sheet is asked for, under the host's root: <c>/css/app.css</c>.</summary>
    public PathString RequestPath { get; }

    /// <summary>The sheet's content hash, for its <c>?v=</c>; null when the app has none.</summary>
    public string? Hash => Current()?.Hash;

    /// <summary>
    ///     The stylesheet of <paramref name="app" />, where its Rask.Stylesheet metadata says it is served, in the
    ///     host's web root; <c>css/app.css</c> when the assembly does not say.
    /// </summary>
    public static AppStylesheet For(IWebHostEnvironment environment, Assembly? app) =>
        new(environment.WebRootFileProvider, (app is null ? null : RaskDocument.StylesheetOf(app)) ?? "css/app.css");

    /// <summary>Registers the sheet and the middleware that answers for it, once.</summary>
    public static void Register(IServiceCollection services)
    {
        services.TryAddSingleton(static sp =>
            For(sp.GetRequiredService<IWebHostEnvironment>(), Assembly.GetEntryAssembly()));
        services.TryAddEnumerable(ServiceDescriptor.Transient<IStartupFilter, Startup>());
    }

    /// <summary>Answers a GET or HEAD for the sheet; anything else, or an app with no sheet, goes on.</summary>
    /// <remarks>
    ///     At the front of the pipeline the path still carries the app's path base (MapRask's, or UsePathBase's), so
    ///     both <c>/css/app.css</c> and <c>{base}/css/app.css</c> are the sheet.
    /// </remarks>
    public Task Answer(HttpContext context, RequestDelegate next) =>
        (HttpMethods.IsGet(context.Request.Method) || HttpMethods.IsHead(context.Request.Method))
        && IsSheet(context.Request.Path)
        && Current() is { } asset
            ? asset.Serve(context)
            : next(context);

    private bool IsSheet(PathString path) =>
        path.Equals(RequestPath, StringComparison.OrdinalIgnoreCase)
        || (Rask.Core.Live.LiveOptions.PathBase is { Length: > 0 } pathBase
            && path.Equals(new PathString(pathBase).Add(RequestPath), StringComparison.OrdinalIgnoreCase));

    private HashedAsset? Current()
    {
        var now = Environment.TickCount64;
        if (Volatile.Read(ref _looked) && now - Volatile.Read(ref _lookedAt) < _lookAgainMs)
        {
            return _current?.Asset;
        }

        lock (_gate)
        {
            if (_looked && now - _lookedAt < _lookAgainMs)
            {
                return _current?.Asset;
            }

            var file = _files.GetFileInfo(_path);
            if (!file.Exists || file.IsDirectory)
            {
                _current = null;
            }
            else if (_current is not { } known || known.Modified != file.LastModified || known.Length != file.Length)
            {
                _current = Read(file);
            }

            Volatile.Write(ref _lookedAt, now);
            Volatile.Write(ref _looked, true);
            return _current?.Asset;
        }
    }

    private static Snapshot Read(IFileInfo file)
    {
        using var stream = file.CreateReadStream();
        using var copy = new MemoryStream(file.Length > 0 ? (int)file.Length : 0);
        stream.CopyTo(copy);
        var bytes = copy.ToArray();
        return new Snapshot(new HashedAsset(bytes, ContentType, HashedAsset.HashOf(bytes)), file.LastModified, file.Length);
    }

    private sealed record Snapshot(HashedAsset Asset, DateTimeOffset Modified, long Length);

    /// <summary>
    ///     Puts <see cref="Answer" /> at the very front of the pipeline, ahead of the app's own static files: a
    ///     startup filter's middleware runs before anything <c>Program.cs</c> adds.
    /// </summary>
    private sealed class Startup(AppStylesheet stylesheet) : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use(stylesheet.Answer);
            next(app);
        };
    }
}
