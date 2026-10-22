using System.Text;

namespace Rask.Printing.Tests;

public sealed class PrintHoldTests
{
    private const string Hold = "<style id=\"rask-print-hold\">";

    [Fact]
    public void A_document_is_held_right_after_its_doctype()
    {
        const string html = "<!DOCTYPE html><html lang=\"hu\"><head></head><body>x</body></html>";

        var held = Encoding.UTF8.GetString(PrintHold.Utf8(html));

        Assert.StartsWith("<!DOCTYPE html>" + Hold, held, StringComparison.Ordinal);
        Assert.EndsWith("</style><html lang=\"hu\"><head></head><body>x</body></html>", held, StringComparison.Ordinal);
    }

    [Fact]
    public void A_doctype_after_whitespace_and_a_comment_stays_ahead_of_the_hold()
    {
        const string html = "\n<!-- report --> <!doctype html><p>x</p>";

        var held = Encoding.UTF8.GetString(PrintHold.Utf8(html));

        Assert.StartsWith("\n<!-- report --> <!doctype html>" + Hold, held, StringComparison.Ordinal);
    }

    [Fact]
    public void A_document_without_a_doctype_is_held_from_its_start()
    {
        const string html = "<p>x</p>";

        var held = Encoding.UTF8.GetString(PrintHold.Utf8(html));

        Assert.StartsWith(Hold, held, StringComparison.Ordinal);
        Assert.EndsWith("</style><p>x</p>", held, StringComparison.Ordinal);
    }
}
