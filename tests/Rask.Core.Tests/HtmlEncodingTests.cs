using System.Text;
using System.Text.Encodings.Web;
using Rask.Core.Live;

namespace Rask.Core.Tests;

public partial class HtmlEncodingTests : global::Rask.Core.RaskMarkup
{
    public static TheoryData<string> Values => new()
    {
        "",
        "plain-ascii 123",
        "display:flex;gap:8px;",
        "don't",
        "Tom & Jerry <b>\"x\"</b>",
        "Árvíztűrő tükörfúrógép",
        "emoji 😀 pair",
        "lone \uD800 high and \uDC00 low",
        "ends in a lone high \uD83D",
        "tel:+123",
        new string('ő', 300),
        new string('a', 255) + "😀" + new string(';', 300),
    };

    [Theory]
    [MemberData(nameof(Values))]
    public void Appending_encoded_text_writes_what_the_encoder_returns(string value)
    {
        var sb = new StringBuilder("<");

        HtmlSerializer.AppendEncoded(sb, value);

        Assert.Equal("<" + HtmlEncoder.Default.Encode(value), sb.ToString());
    }

    [Theory]
    [MemberData(nameof(Values))]
    public void Appending_an_encoded_span_writes_what_the_encoder_returns(string value)
    {
        var sb = new StringBuilder();

        HtmlSerializer.AppendEncoded(sb, $"[{value}]".AsSpan(1, value.Length));

        Assert.Equal(HtmlEncoder.Default.Encode(value), sb.ToString());
    }

    [Fact]
    public void A_trusted_url_reaches_the_frame_writer_without_its_marker()
    {
        var writer = new FrameWriter();
        var html = new StringBuilder();

        using (FrameSinkScope.Push(writer))
        {
            HtmlSerializer.Serialize(A.Href(RaskUrl.Trusted("/x?a=1&b=2")), html);
        }

        Assert.Equal("<a href=\"/x?a=1&amp;b=2\"></a>", html.ToString());
        Assert.Contains(writer.WrittenSpan.ToArray(), f => f is { Name: "href", Value: "/x?a=1&b=2" });
    }
}
