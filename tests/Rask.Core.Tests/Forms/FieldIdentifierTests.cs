using Rask.Core.Forms;

namespace Rask.Core.Tests.Forms;

public class FieldIdentifierTests
{
    [Fact]
    public void Constructor_NullModel_Throws() =>
        Assert.Throws<ArgumentNullException>(() => new FieldIdentifier(null!, "X"));

    [Fact]
    public void Constructor_NullFieldName_Throws() =>
        Assert.Throws<ArgumentNullException>(() => new FieldIdentifier(new object(), null!));

    [Fact]
    public void Equals_SameModelReferenceAndFieldName_ReturnsTrue()
    {
        var m = new Model();
        var a = new FieldIdentifier(m, "Name");
        var b = new FieldIdentifier(m, "Name");

        Assert.True(a.Equals(b));
        Assert.True(a.Equals((object)b));
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }

    [Fact]
    public void Equals_DifferentInstanceSameValues_ReturnsFalse()
    {
        var a = new FieldIdentifier(new Model { Name = "x" }, "Name");
        var b = new FieldIdentifier(new Model { Name = "x" }, "Name");

        Assert.False(a.Equals(b));
    }

    [Fact]
    public void Equals_DifferentFieldName_ReturnsFalse()
    {
        var m = new Model();
        var a = new FieldIdentifier(m, "Name");
        var b = new FieldIdentifier(m, "Other");

        Assert.False(a.Equals(b));
    }

    [Fact]
    public void Equals_NonFieldIdentifier_ReturnsFalse()
    {
        var a = new FieldIdentifier(new Model(), "Name");

        Assert.False(a.Equals("not-a-field"));
    }

    [Fact]
    public void ToString_FormatIs_TypeNameDotFieldName()
    {
        var fid = new FieldIdentifier(new Model(), "Name");

        Assert.Equal("Model.Name", fid.ToString());
    }

    private sealed class Model
    {
        public string Name { get; set; } = "";
    }
}
