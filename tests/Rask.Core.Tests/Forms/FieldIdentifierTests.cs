using Rask.Core.Forms;

namespace Rask.Core.Tests.Forms;

public class FieldIdentifierTests
{
    [Fact]
    public void A_null_model_throws() =>
        Assert.Throws<ArgumentNullException>(() => new FieldIdentifier(null!, "X"));

    [Fact]
    public void A_null_field_name_throws() =>
        Assert.Throws<ArgumentNullException>(() => new FieldIdentifier(new object(), null!));

    [Fact]
    public void The_same_model_reference_and_field_name_are_equal()
    {
        var m = new Model();
        var a = new FieldIdentifier(m, "Name");
        var b = new FieldIdentifier(m, "Name");

        Assert.True(a.Equals(b));
        Assert.True(a.Equals((object)b));
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }

    [Fact]
    public void Different_model_instances_with_the_same_values_are_not_equal()
    {
        var a = new FieldIdentifier(new Model { Name = "x" }, "Name");
        var b = new FieldIdentifier(new Model { Name = "x" }, "Name");

        Assert.False(a.Equals(b));
    }

    [Fact]
    public void Different_field_names_are_not_equal()
    {
        var m = new Model();
        var a = new FieldIdentifier(m, "Name");
        var b = new FieldIdentifier(m, "Other");

        Assert.False(a.Equals(b));
    }

    [Fact]
    public void A_field_identifier_never_equals_another_kind_of_object()
    {
        var a = new FieldIdentifier(new Model(), "Name");

        Assert.False(a.Equals("not-a-field"));
    }

    [Fact]
    public void It_prints_as_the_type_name_dot_the_field_name()
    {
        var fid = new FieldIdentifier(new Model(), "Name");

        Assert.Equal("Model.Name", fid.ToString());
    }

    private sealed class Model
    {
        public string Name { get; set; } = "";
    }
}
