using System.ComponentModel.DataAnnotations;

namespace Rask.UiTests.Components;

/// <summary><c>Ui.Input</c> bound to a <c>[RegularExpression]</c> member says it as the input's <c>pattern</c>.</summary>
public partial class UiFieldPatternTests : global::Rask.Core.RaskMarkup
{
    [Fact]
    public void A_bound_input_says_the_pattern_its_field_must_match()
    {
        var vehicle = new Vehicle();

        var html = Ui.Input.Bind(() => vehicle.Plate).Label("Plate").ToHtml();

        Assert.Contains("pattern=\"[A-Z]{3}\\p{Nd}{3}\"", html, StringComparison.Ordinal);
    }

    private sealed class Vehicle
    {
        [RegularExpression(@"^[A-Z]{3}\d{3}$")]
        public string? Plate { get; set; }
    }
}
