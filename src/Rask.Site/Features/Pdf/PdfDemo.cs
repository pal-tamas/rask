namespace Rask.Site.Features;

// rask.sh is static files, so there is no server here to print on. The PDF behind the button was printed from the
// RefundReport below by Rask.Pdf — a server with Pdf.From — and is checked by the build's tests to still be that
// report (tests/Rask.Pdf.Tests, SiteDemoPdfTests). In an app, the button's handler is the one line in the comment.
public sealed partial class PdfDemo : Component
{
    /// <summary>Where the printed report is served, beside the site's other static files.</summary>
    public const string File = "/demos/pdf/visszaterites.pdf";

    protected override Component? Render() =>
        Div[
            // In an app: Ui.Button.OnClick(async () => Download.File("visszaterites.pdf", await Pdf.From(Routes.RefundReportPage()), Pdf.ContentType))
            Ui.Button.Primary.Icon(Ui.IconName.ArrowDownTray).Id("download-pdf").Href(File)
                .Attributes(("download", "visszaterites.pdf"))["Letöltés PDF-ben"],
            Div.Class("mt-6 max-h-[32rem] overflow-auto rounded-lg border border-zinc-200 dark:border-white/10 p-4")[RefundReport]
        ];
}
