using System.Security.Claims;
using Rask.Core;
using Rask.Core.Printing;
using Rask.Core.Routing;

namespace Rask.Printing;

/// <summary>
/// A page of the app to PDF: rendered by the host for the caller — never anyone else — then printed with the
/// app's own stylesheet and fonts, and nothing else, reachable.
/// </summary>
internal sealed class PagePrinting(IPrintablePages pages, ChromiumPrinter printer, ClaimsPrincipal caller)
{
    public async Task<byte[]> Print(RouteUrl route, PdfOptions? options, CancellationToken cancellation)
    {
        var page = await pages.Render(route, caller, cancellation).ConfigureAwait(false);
        return await Print(page, options, cancellation).ConfigureAwait(false);
    }

    public async Task<byte[]> Print(Component component, PdfOptions? options, CancellationToken cancellation)
    {
        var page = await pages.Render(component, caller, cancellation).ConfigureAwait(false);
        return await Print(page, options, cancellation).ConfigureAwait(false);
    }

    private Task<byte[]> Print(PrintablePage page, PdfOptions? options, CancellationToken cancellation) => printer.Print(
        new PrintDocument(page.Html, page.Path, options ?? new PdfOptions(), new Files(pages)), cancellation);

    private sealed class Files(IPrintablePages pages) : IPrintAssets
    {
        public async ValueTask<PrintAsset?> Find(string path, CancellationToken cancellation) =>
            await pages.File(path, cancellation).ConfigureAwait(false) is { } file ? new PrintAsset(file.Body, file.ContentType) : null;
    }
}
