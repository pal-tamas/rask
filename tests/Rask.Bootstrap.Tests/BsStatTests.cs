namespace Rask.Bootstrap.Tests;

// Rendered-HTML assertions for BsStat — the card wrapper, the label/value/caption structure, the tone
// applied to the VALUE rather than the card, the optional icon, and the linked variant.
public partial class BsStatTests : global::Rask.Core.RaskMarkup
{
    [Fact]
    public void Stat_RendersLabelAboveValueInACard() =>
        Assert.Equal(
            "<div class=\"card h-100\"><div class=\"card-body py-3\">"
            + "<div class=\"d-flex align-items-center gap-2 text-body-secondary text-uppercase small fw-semibold\">"
            + "<span>Failed</span></div>"
            + "<div class=\"fs-3 fw-semibold lh-1 mt-2\">0</div>"
            + "</div></div>",
            BsStat.Value("0").Label("Failed").ToHtml());

    [Fact]
    // The tone colours the number, not the card: one red value among grey ones reads as a signal, whereas
    // a wall of coloured panels reads as decoration.
    public void Stat_Tone_ColorsTheValueNotTheCard()
    {
        var html = BsStat.Value("3").Label("Failed").Tone(BsColor.Danger).ToHtml();

        Assert.Contains("<div class=\"fs-3 fw-semibold lh-1 mt-2 text-danger\">3</div>", html, StringComparison.Ordinal);
        Assert.Contains("<div class=\"card h-100\">", html, StringComparison.Ordinal);   // card itself stays neutral
        Assert.DoesNotContain("text-bg-danger", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Stat_Caption_RendersUnderTheValue() =>
        Assert.Contains(
            "<div class=\"small text-body-secondary mt-1\">after 25 attempts</div>",
            BsStat.Value("3").Label("Failed").Caption("after 25 attempts").ToHtml(),
            StringComparison.Ordinal);

    [Fact]
    public void Stat_Icon_RendersBesideTheLabel() =>
        Assert.Contains(
            // BsIcon marks itself aria-hidden — the label beside it already carries the meaning.
            "<i class=\"bi bi-envelope\" aria-hidden=\"true\"></i><span>Mail</span>",
            BsStat.Value("1").Label("Mail").Icon(BsIconName.Envelope).ToHtml(),
            StringComparison.Ordinal);

    [Fact]
    // A linked tile must be a link without looking like body text, and must keep the card intact inside it.
    public void Stat_Href_WrapsTheCardInAResetLink()
    {
        var html = BsStat.Value("2").Label("Jobs").Href("/_ops/queues/jobs").ToHtml();

        Assert.StartsWith(
            "<a class=\"text-decoration-none text-reset d-block h-100\" href=\"/_ops/queues/jobs\">",
            html,
            StringComparison.Ordinal);
        Assert.Contains("<div class=\"card h-100\">", html, StringComparison.Ordinal);
        Assert.EndsWith("</a>", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Stat_MergesUserClassOntoTheCard() =>
        Assert.Contains(
            "<div class=\"card h-100 border-danger\">",
            BsStat.Value("9").Label("Dead letters").Class("border-danger").ToHtml(),
            StringComparison.Ordinal);
}
