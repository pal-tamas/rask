namespace Rask.Html.Tests.Components;

public partial class DoctypeTests : global::Rask.Core.RaskMarkup
{
    [Fact]
    public void Render_Default_ReturnsDoctypeDeclaration() => Assert.Equal("<!DOCTYPE html>", Doctype.ToHtml());
}
