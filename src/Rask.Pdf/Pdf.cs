using System.Security.Claims;
using Microsoft.Extensions.DependencyInjection;
using Rask.Core;
using Rask.Core.Authentication;
using Rask.Core.Printing;
using Rask.Core.Routing;

namespace Rask.Printing;

/// <summary>
/// A PDF of a page or a component of the app, drawn by the headless Chromium installed beside it:
/// <c>var pdf = await Pdf.From(Routes.RefundReport(id));</c>
/// </summary>
/// <remarks>
/// The page is rendered on the server as its first GET would be — static HTML, no live session — for the user
/// making the call, after its <c>[Authorize]</c> admits them: a page they could not open is never printed.
/// </remarks>
public static class Pdf
{
    /// <summary>The media type of a PDF, for a download's <c>Content-Type</c>.</summary>
    public const string ContentType = "application/pdf";

    /// <summary>Prints the app's page at <paramref name="route"/>, as the calling user sees it.</summary>
    /// <exception cref="UnauthorizedAccessException">The page's authorization does not admit the caller.</exception>
    /// <exception cref="TimeoutException">The PDF was not ready within <c>Rask:Pdf:Timeout</c>.</exception>
    public static Task<byte[]> From(RouteUrl route, PdfOptions? options = null) =>
        Printing().Print(route, options, Ambient.CancellationToken);

    /// <summary>Prints <paramref name="component"/> as a whole document of the app — its shell, stylesheet and fonts.</summary>
    /// <exception cref="TimeoutException">The PDF was not ready within <c>Rask:Pdf:Timeout</c>.</exception>
    public static Task<byte[]> From(Component component, PdfOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(component);
        return Printing().Print(component, options, Ambient.CancellationToken);
    }

    // The caller is whoever the work in progress belongs to: the session's user in a handler or a render, the
    // request's in an endpoint. Never a user the caller names.
    private static PagePrinting Printing()
    {
        var services = Ambient.Services ?? throw new InvalidOperationException(
            "Pdf.From was called outside any work in progress — a handler, a render, a request — so there is no app "
            + "and no user to print for.");
        var pages = services.GetService<IPrintablePages>() ?? throw new InvalidOperationException(
            "Pdf.From renders the app's pages on the server, and this app has no Rask server host. Print from a "
            + "Rask.Server app (RaskApp, or builder.Services.AddRask()).");
        var printer = services.GetService<ChromiumPrinter>() ?? throw new InvalidOperationException(
            "Pdf.From is not running in this app: call builder.Services.AddRaskPdf() (on a RaskApp, "
            + "RaskApp.Create(args, b => b.Services.AddRaskPdf())).");
        var caller = services.GetService<IUserProvider>()?.Current ?? new ClaimsPrincipal(new ClaimsIdentity());
        return new PagePrinting(pages, printer, caller);
    }
}
