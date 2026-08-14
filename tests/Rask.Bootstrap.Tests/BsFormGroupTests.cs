namespace Rask.Bootstrap.Tests;

// Rendered-HTML assertions for the hand-composition layout helpers: BsFormGroup (the .mb-3 field
// spacer) and BsFormLabel (a .form-label whose For ties it to a control id).
public partial class BsFormGroupTests : global::Rask.Core.RaskMarkup
{
    [Fact]
    public void FormGroup_WrapsItemsInMb3() =>
        Assert.Equal("<div class=\"mb-3\"><span>x</span></div>", BsFormGroup[Span["x"]].ToHtml());

    [Fact]
    public void FormGroup_MergesUserClassAndId() =>
        Assert.Equal("<div id=\"g\" class=\"mb-3 border\"><span>x</span></div>",
            BsFormGroup.Id("g").Class("border")[Span["x"]].ToHtml());

    [Fact]
    public void FormLabel_RendersFormLabelTiedToControl() =>
        Assert.Equal("<label class=\"form-label\" for=\"email\">Email</label>",
            BsFormLabel.For("email")["Email"].ToHtml());

    [Fact]
    public void FormLabel_MergesUserClassAndId() =>
        Assert.Equal("<label id=\"l\" class=\"form-label fw-bold\" for=\"e\">E</label>",
            BsFormLabel.For("e").Id("l").Class("fw-bold")["E"].ToHtml());
}
