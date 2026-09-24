namespace Rask.Core.Tests.Components;

public partial class DoctypeTests : global::Rask.Core.RaskMarkup
{
    [Fact]
    public void It_renders_the_doctype_declaration() => Assert.Equal("<!DOCTYPE html>", Doctype.ToHtml());
}
