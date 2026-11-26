using System.Text.RegularExpressions;
using Rask.Core.Dom.Build;

namespace Rask.Core.Tests.Dom;

public sealed partial class DocLinkTests
{
    [Fact]
    public void A_link_s_ampersand_is_escaped_in_the_doc_comment()
    {
        const string url = "https://html.spec.whatwg.org/multipage/sections.html#heading-levels-&-offsets";

        var link = DomEmitter.SeeHref(url, "Spec");

        Assert.Equal("<see href=\"https://html.spec.whatwg.org/multipage/sections.html#heading-levels-&amp;-offsets\">Spec</see>", link);
    }

    [Fact]
    public void No_generated_doc_link_carries_a_bare_ampersand_when_MDN_s_links_do()
    {
        // whatwg anchors such as #heading-levels-&-offsets: here every spec and MDN link MDN hands over has one.
        var snapshot = MdnSnapshot.Text
            .Replace("\"spec\": \"https://", "\"spec\": \"https://x/&-", StringComparison.Ordinal)
            .Replace("\"mdn\": \"https://", "\"mdn\": \"https://x/&-", StringComparison.Ordinal);

        var files = DomEmitter.Emit(snapshot, new Partials());

        Assert.Contains(files, f => f.Value.Contains("https://x/&amp;-", StringComparison.Ordinal));
        Assert.DoesNotContain(files, f => BareAmpersandInLink().IsMatch(f.Value));
    }

    [GeneratedRegex("href=\"[^\"]*&(?!amp;|lt;|gt;|quot;|apos;)")]
    private static partial Regex BareAmpersandInLink();
}
