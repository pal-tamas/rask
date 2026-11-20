namespace Rask.UiTests.Components;

/// <summary>
///     The editor's value is cut to its own schema on the way in (the first paint) and on the way out (a change):
///     a stored value that did not come from the editor cannot run in the page.
/// </summary>
public class UiEditorHtmlTests
{
    [Theory]
    [InlineData("<p>Hello <strong>world</strong></p>")]
    [InlineData("<h2 style=\"text-align: center\">Title</h2><ul><li><p>one</p></li></ul>")]
    [InlineData("<p><a target=\"_blank\" rel=\"noopener noreferrer nofollow\" href=\"https://rask.sh/a?b=1&amp;c=2\">link</a></p>")]
    [InlineData("<pre><code class=\"language-csharp\">var x = 1 &lt; 2;</code></pre><blockquote><p>q</p></blockquote>")]
    [InlineData("<p>a<br>b</p><hr><ol start=\"3\"><li><p>x</p></li></ol>")]
    public void What_the_editor_itself_writes_comes_out_as_it_went_in(string html)
    {
        var cleaned = UiEditorHtml.Clean(html);

        Assert.Equal(html, cleaned);
    }

    [Theory]
    [InlineData("<p>a</p><script>alert(1)</script><p>b</p>", "<p>a</p><p>b</p>")]
    [InlineData("<img src=x onerror=alert(1)><p>a</p>", "<p>a</p>")]
    [InlineData("<p onclick=\"alert(1)\" class=\"x\">a</p>", "<p>a</p>")]
    [InlineData("<a href=\"javascript:alert(1)\">a</a>", "<a>a</a>")]
    [InlineData("<a href=\"java&#9;script:alert(1)\">a</a>", "<a>a</a>")]
    [InlineData("<a href=\" JaVaScRiPt:alert(1)\">a</a>", "<a>a</a>")]
    [InlineData("<a href=\"data:text/html,<script>alert(1)</script>\">a</a>", "<a>a</a>")]
    [InlineData("<svg><script>alert(1)</script></svg><p>a</p>", "<p>a</p>")]
    [InlineData("<!--<script>alert(1)</script>--><p>a</p>", "<p>a</p>")]
    [InlineData("<p title=\"\"><iframe srcdoc=\"<script>alert(1)</script>\"></iframe></p>", "<p></p>")]
    [InlineData("<p style=\"background:url(javascript:alert(1)); text-align: right\">a</p>", "<p style=\"text-align: right\">a</p>")]
    [InlineData("<div><span>kept text</span></div>", "kept text")]
    [InlineData("<p>a < b</p>", "<p>a &lt; b</p>")]
    [InlineData("<p><strong>open", "<p><strong>open</strong></p>")]
    [InlineData("</p></div><p>a</p>", "<p>a</p>")]
    [InlineData("<a href='x\" onmouseover=\"alert(1)'>a</a>", "<a href=\"x&quot; onmouseover=&quot;alert(1)\">a</a>")]
    public void What_the_schema_does_not_write_is_dropped(string html, string cleaned)
    {
        var kept = UiEditorHtml.Clean(html);

        Assert.Equal(cleaned, kept);
    }
}
