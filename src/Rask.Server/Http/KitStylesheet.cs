using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Rask.Server.Http;

/// <summary>
///     The UI kit's precompiled stylesheet (<see cref="UiStylesheet.Css" />), served at <see cref="UiStylesheet.Path" />
///     out of the kit's own assembly: compressed, revalidated by its ETag, and kept for a year under
///     <c>?v={version}</c>.
/// </summary>
/// <remarks>
///     <para>
///         A page links this sheet whenever the kit is on and the app's own Tailwind build does not carry the kit
///         (<see cref="RaskDocument" />). Before, only <c>RaskUiWriteStylesheet</c> put a file at that address, so an
///         app with neither linked a sheet nothing served (#1315): a 404 on every page and an unstyled kit. Serving
///         the bytes the kit already embeds needs no build step, no project-reference ordering and no file in the
///         app's tree, and they are the very bytes <see cref="UiStylesheet.Version" /> is the hash of.
///     </para>
///     <para>
///         Answered ahead of the app's pipeline, like <see cref="AppStylesheet" />, so a hand-wired host that links
///         <see cref="UiStylesheet.Href" /> itself gets it too. An app whose sheet compiles the kit in links none of
///         this, and nothing asks for it.
///     </para>
/// </remarks>
internal sealed class KitStylesheet
{
    private const string ContentType = "text/css; charset=utf-8";

    private static readonly PathString RequestPath = new(UiStylesheet.Path);

    // Built on the first request: an app that never links the sheet never copies or compresses it.
    private readonly Lazy<HashedAsset?> _asset = new(static () =>
        UiStylesheet.Css.Length == 0
            ? null
            : new HashedAsset(Encoding.UTF8.GetBytes(UiStylesheet.Css), ContentType, UiStylesheet.Version));

    /// <summary>Registers the sheet and the middleware that answers for it, once.</summary>
    public static void Register(IServiceCollection services)
    {
        services.TryAddSingleton<KitStylesheet>();
        services.TryAddEnumerable(ServiceDescriptor.Transient<IStartupFilter, Startup>());
    }

    /// <summary>Answers a GET or HEAD for the sheet; anything else goes on.</summary>
    /// <remarks>
    ///     At the front of the pipeline the path still carries the app's path base, so both
    ///     <c>/css/rask-ui.css</c> and <c>{base}/css/rask-ui.css</c> are the sheet — the address
    ///     <see cref="UiStylesheet.Href" /> writes under that base.
    /// </remarks>
    public Task Answer(HttpContext context, RequestDelegate next) =>
        (HttpMethods.IsGet(context.Request.Method) || HttpMethods.IsHead(context.Request.Method))
        && IsSheet(context.Request.Path)
        && _asset.Value is { } asset
            ? asset.Serve(context)
            : next(context);

    private static bool IsSheet(PathString path) =>
        path.Equals(RequestPath, StringComparison.OrdinalIgnoreCase)
        || (Rask.Core.Live.LiveOptions.PathBase is { Length: > 0 } pathBase
            && path.Equals(new PathString(pathBase).Add(RequestPath), StringComparison.OrdinalIgnoreCase));

    /// <summary>Puts <see cref="Answer" /> at the front of the pipeline, ahead of the app's own static files.</summary>
    private sealed class Startup(KitStylesheet stylesheet) : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use(stylesheet.Answer);
            next(app);
        };
    }
}
