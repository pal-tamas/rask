using System.Text.RegularExpressions;
using Rask.Core;
using Rask.Testing;

namespace Rask.Ui.Tests;

/// <summary>
///     The terminal mockup. Its whole reason to exist over a hand-rolled <c>&lt;pre&gt;</c> is that the
///     prompt is drawn by CSS rather than written into the document, so a reader who selects the command
///     copies the command and not the <c>$</c> in front of it. That is a claim about the MARKUP — the
///     prefix has to reach the page as an attribute and never as text — which is what these pin.
/// </summary>
public sealed class UiMockupCodeTests
{
    private static readonly (string Prefix, string Text)[] TwoLines =
    [
        ("#", "a comment"),
        ("$", "rask new MyApp"),
    ];

    [Fact]
    public void Each_line_carries_its_prompt_as_an_attribute()
    {
        // Matched with a pattern rather than a literal: Key writes data-rask-k onto the same element, and
        // the framework's attribute order puts it first, so the two are not adjacent.
        var html = Html(TwoLines, null);

        Assert.Matches(new Regex("<pre[^>]*\\sdata-prefix=\"#\""), html);
        Assert.Matches(new Regex("<pre[^>]*\\sdata-prefix=\"\\$\""), html);
    }

    [Fact]
    public void The_prompt_is_never_written_into_the_text()
    {
        // The point of the component. A prompt in the text is a broken paste: the reader selects the
        // command, copies `$ rask new MyApp`, and the shell answers `$: command not found`.
        var text = Regex.Replace(Html(TwoLines, null), "<[^>]*>", "");

        Assert.DoesNotContain("$", text, StringComparison.Ordinal);
        Assert.DoesNotContain("#", text, StringComparison.Ordinal);
        Assert.Contains("rask new MyApp", text, StringComparison.Ordinal);
    }

    [Fact]
    public void The_command_is_the_code_elements_only_content()
    {
        Assert.Contains("<code>rask new MyApp</code>", Html(TwoLines, null), StringComparison.Ordinal);
    }

    [Fact]
    public void A_call_site_class_is_added_to_the_daisyui_one_rather_than_replacing_it()
    {
        // mockup-code is what every rule in the kit's sheet keys on, so losing it to a call-site class
        // renders an unstyled stack of <pre> that still contains all the right text.
        var classes = Regex.Match(Html(TwoLines, "term text-left"), "class=\"([^\"]*)\"").Groups[1].Value;

        Assert.Contains("mockup-code", classes, StringComparison.Ordinal);
        Assert.Contains("term", classes, StringComparison.Ordinal);
        Assert.Contains("text-left", classes, StringComparison.Ordinal);
    }

    private static string Html((string Prefix, string Text)[] lines, string? cls) =>
        RaskTest.Render(new MockupHost { MockupLines = lines, MockupClass = cls }).Html;
}

/// <summary>
///     A markup host, for the same reason <see cref="Host" /> is one: the chain's entry for a component
///     only exists inside a markup host, so a bare <c>UiMockupCode</c> in a test class is the type.
/// </summary>
internal sealed partial class MockupHost : Component
{
    public required IReadOnlyList<(string Prefix, string Text)> MockupLines { get; set; }

    public string? MockupClass { get; set; }

    protected override Component? Render() => UiMockupCode.Lines(MockupLines).Class(MockupClass);
}
