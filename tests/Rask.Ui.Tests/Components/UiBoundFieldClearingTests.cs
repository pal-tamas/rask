using Rask.Core;
using Rask.Core.Forms;
using Rask.Core.Routing;
using Page = Rask.Testing.Page;

namespace Rask.UiTests.Components;

/// <summary>A form over a model whose Nick may be cleared, registered as a generated operation form registers it.</summary>
[Route("/ui-bound-field/clearing")]
public sealed partial class BoundFieldClearingPage : Component
{
    private readonly ClearingInput _input = new();

    static BoundFieldClearingPage() =>
        RaskValidation.RegisterClearing(typeof(ClearingInput), static property => property == "Nick" ? "NickCleared" : null);

    /// <summary>The input the page is over.</summary>
    public ClearingInput Input => _input;

    /// <inheritdoc />
    protected override Component? Render() =>
        Form.Model(_input)[
            Ui.Field.Bind(() => _input.Nick),
            Ui.Field.Bind(() => _input.Name)
        ];

    /// <summary>A nickname that may be cleared, and a name that may not.</summary>
    public sealed class ClearingInput
    {
        /// <summary>The nickname; empty keeps the stored one.</summary>
        public string? Nick { get; set; }

        /// <summary>Whether the nickname is to be cleared.</summary>
        public bool NickCleared { get; set; }

        /// <summary>The name.</summary>
        public string? Name { get; set; }
    }
}

/// <summary>F-09: a field whose value may be taken away gets a Clear beside it, bound to the model's own flag.</summary>
public sealed partial class UiBoundFieldClearingTests : global::Rask.Core.RaskMarkup
{
    [Fact]
    public void A_field_that_may_be_cleared_has_a_Clear_beside_it_and_one_that_may_not_has_none()
    {
        var page = Page.Visit("/ui-bound-field/clearing");

        var clears = page.FindAll("input[type=\"checkbox\"]");

        Assert.Single(clears);
        Assert.Contains("Clear", page.Html, StringComparison.Ordinal);
    }
}
