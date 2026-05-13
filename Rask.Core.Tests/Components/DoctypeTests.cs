using Rask.Core.Components;

namespace Rask.Core.Tests.Components;

public class DoctypeTests
{
    [Fact]
    public void Render_Default_ReturnsDoctypeDeclaration() => Assert.Equal("<!DOCTYPE html>", Doctype().ToHtml());
}
