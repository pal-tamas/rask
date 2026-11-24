using System.ComponentModel.DataAnnotations;
using Rask.Core.Forms;

namespace Rask.Core.Tests.Forms;

// A bound text input is told its field's regular expression as its `pattern`, where the browser reads it as .NET does.
public partial class PatternInputAttributesTests : global::Rask.Core.RaskMarkup
{
    static PatternInputAttributesTests()
    {
        RaskValidation.RegisterFieldShapes(typeof(Shaped), static name => name switch
        {
            nameof(Shaped.Plate) => new FieldShape(FieldKind.Text) { Pattern = @"^[A-Z]{3}\d{3}$" },
            nameof(Shaped.Pieces) => new FieldShape(FieldKind.Whole) { Pattern = @"^\d+$" },
            _ => null,
        });
    }

    [Fact]
    public void A_regular_expression_member_writes_its_pattern()
    {
        var m = new Annotated();

        var html = Page.Render(() => Input.Bind(() => m.Code)).Render();

        Assert.Contains("pattern=\"[A-Z]{2,3}\"", html, StringComparison.Ordinal);
        Assert.Contains(" data-rask-pattern", html, StringComparison.Ordinal);
    }

    [Fact]
    public void A_pattern_the_author_set_wins()
    {
        var m = new Annotated();

        var html = Page.Render(() => Input.Bind(() => m.Code).Pattern("[a-z]{4}")).Render();

        Assert.Contains("pattern=\"[a-z]{4}\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("[A-Z]{2,3}", html, StringComparison.Ordinal);
        Assert.DoesNotContain("data-rask-pattern", html, StringComparison.Ordinal);
    }

    [Fact]
    public void A_regular_expression_the_browser_reads_otherwise_writes_none()
    {
        var m = new Annotated();

        var html = Page.Render(() => Input.Bind(() => m.Word)).Render();

        Assert.DoesNotContain("pattern", html, StringComparison.Ordinal);
    }

    [Fact]
    public void A_registered_shape_tells_its_pattern_in_the_browsers_dialect()
    {
        var m = new Shaped();

        var html = Page.Render(() => Input.Bind(() => m.Plate)).Render();

        Assert.Contains("pattern=\"[A-Z]{3}\\p{Nd}{3}\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public void A_number_input_is_told_no_pattern()
    {
        var m = new Shaped();

        var html = Page.Render(() => Input.Bind(() => m.Pieces)).Render();

        Assert.Contains("type=\"number\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("pattern=", html, StringComparison.Ordinal);
    }

    private sealed class Annotated
    {
        [RegularExpression(@"^[A-Z]{2,3}$")]
        public string? Code { get; set; }

        [RegularExpression(@"(?i)\bword\b")]
        public string? Word { get; set; }
    }

    private sealed class Shaped
    {
        public string? Plate { get; set; }

        public int Pieces { get; set; }
    }
}
