using System.Globalization;
using System.Linq.Expressions;
using Rask.Core.Forms;

namespace Rask.Core.Tests.Forms;

// Guards the public binding API surface (ExpressionAccessor / BindingHelpers) that custom form-bound
// controls — like the BsMultiSelect example component — rely on. These types were promoted from internal
// to public; this test fails if they regress to internal or change shape.
public class PublicBindingApiTests
{
    [Fact]
    public void ExpressionAccessor_resolves_the_target_getter_and_field()
    {
        var m = new Model();
        var acc = ExpressionAccessor.Parse((Expression<Func<string>>)(() => m.Name));

        Assert.Same(m, acc.Target);
        Assert.Equal("Name", acc.PropertyName);
        Assert.Equal(typeof(string), acc.PropertyType);
        Assert.Equal("Ada", acc.Getter());
        Assert.Equal(new FieldIdentifier(m, "Name"), acc.Field);
    }

    [Fact]
    public void ExpressionAccessor_binds_a_collection_property()
    {
        var m = new Model();
        var acc = ExpressionAccessor.Parse((Expression<Func<ICollection<string>>>)(() => m.Tags));

        Assert.Same(m.Tags, acc.Getter());
        Assert.Equal("Tags", acc.PropertyName);
    }

    [Theory]
    [InlineData(null, "")]
    [InlineData("hi", "hi")]
    [InlineData(42, "42")]
    public void FormatValue_formats_the_common_values(object? value, string expected) =>
        Assert.Equal(expected, BindingHelpers.FormatValue(value));

    [Fact]
    public void FormatValue_uses_the_invariant_culture()
    {
        var prev = Thread.CurrentThread.CurrentCulture;
        Thread.CurrentThread.CurrentCulture = new CultureInfo("de-DE"); // comma decimal separator
        try
        {
            Assert.Equal("1.5", BindingHelpers.FormatValue(1.5));
        }
        finally
        {
            Thread.CurrentThread.CurrentCulture = prev;
        }
    }

    [Fact]
    public void ResolveBindingContext_returns_null_without_an_ambient_context() =>
        // Outside a Form / live render, there's no EditContextScope or LiveRenderContext to resolve.
        Assert.Null(BindingHelpers.ResolveBindingContext(new Model()));

    [Fact]
    public void Including_a_member_adds_it_when_absent_and_is_idempotent()
    {
        var list = new List<string>();

        Assert.True(BindingHelpers.SetCollectionMembership(list, "a", include: true));
        Assert.Equal(["a"], list);

        // Already present → no change, returns false.
        Assert.False(BindingHelpers.SetCollectionMembership(list, "a", include: true));
        Assert.Equal(["a"], list);
    }

    [Fact]
    public void Excluding_a_member_removes_it_when_present_and_is_a_no_op_when_absent()
    {
        var list = new List<string> { "a", "b" };

        Assert.True(BindingHelpers.SetCollectionMembership(list, "a", include: false));
        Assert.Equal(["b"], list);

        // Already absent → no change, returns false.
        Assert.False(BindingHelpers.SetCollectionMembership(list, "a", include: false));
        Assert.Equal(["b"], list);
    }

    [Fact]
    public void Membership_uses_the_comparer_to_remove_the_matched_instance()
    {
        // Two distinct instances that compare equal under the supplied comparer but not by reference.
        var first = new Box(1);
        var list = new List<Box> { first };
        var comparer = new BoxComparer();

        // include=true with a comparer-equal item → treated as present, no duplicate added.
        Assert.False(BindingHelpers.SetCollectionMembership(list, new Box(1), include: true, comparer));
        Assert.Single(list);

        // include=false removes the matched instance (the original `first`).
        Assert.True(BindingHelpers.SetCollectionMembership(list, new Box(1), include: false, comparer));
        Assert.Empty(list);
    }

    [Fact]
    public async Task Notifying_and_validating_with_a_null_context_is_a_no_op() =>
        await BindingHelpers.NotifyAndValidateFieldAsync(null, new FieldIdentifier(new Model(), "Name"));

    [Fact]
    public async Task Notifying_and_validating_marks_the_field_changed_and_touched_and_runs_its_validator()
    {
        var m = new Model { Name = "" };
        var ctx = new EditContext(m);
        var fid = new FieldIdentifier(m, "Name");
        ctx.RegisterFieldValidator(fid,
            (Func<string, IEnumerable<string>>)(v => string.IsNullOrEmpty(v) ? ["required"] : []));

        await BindingHelpers.NotifyAndValidateFieldAsync(ctx, fid);

        Assert.True(ctx.IsModified(fid));
        Assert.True(ctx.IsTouched(fid));
        Assert.Contains("required", ctx.GetValidationMessages(fid));
    }

    private sealed class Model
    {
        public string Name { get; set; } = "Ada";
        public List<string> Tags { get; } = [];
    }

    // A plain class (reference equality, no Equals override) so List<Box>.Remove uses the collection's
    // own equality — which differs from BoxComparer. This proves SetCollectionMembership removes the
    // matched instance, not the (reference-distinct) argument.
    private sealed class Box(int id)
    {
        public int Id { get; } = id;
    }

    private sealed class BoxComparer : IEqualityComparer<Box>
    {
        public bool Equals(Box? x, Box? y) => x?.Id == y?.Id;
        public int GetHashCode(Box obj) => obj.Id;
    }
}
