namespace Rask.Core.Tests.Components;

public partial class DoctypeTests : global::Rask.Core.RaskMarkup
{
    [Fact]
    public void The_doctype_renders_the_html_declaration() => Assert.Equal("<!DOCTYPE html>", Doctype.ToHtml());
}
