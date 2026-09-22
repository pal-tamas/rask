using Rask.Core.Forms;

namespace Rask.Core.Tests.Forms;

public class EditContextScopeTests
{
    [Fact]
    public void Current_defaults_to_null() => Assert.Null(EditContextScope.Current);

    [Fact]
    public void Pushing_sets_Current_and_disposing_restores_the_previous()
    {
        var ctx = new EditContext(new Model());

        using (EditContextScope.Push(ctx))
        {
            Assert.Same(ctx, EditContextScope.Current);
        }

        Assert.Null(EditContextScope.Current);
    }

    [Fact]
    public void Nested_scopes_restore_in_LIFO_order()
    {
        var outer = new EditContext(new Model());
        var inner = new EditContext(new Model());

        using (EditContextScope.Push(outer))
        {
            Assert.Same(outer, EditContextScope.Current);
            using (EditContextScope.Push(inner))
            {
                Assert.Same(inner, EditContextScope.Current);
            }

            Assert.Same(outer, EditContextScope.Current);
        }

        Assert.Null(EditContextScope.Current);
    }

    [Fact]
    public void A_second_dispose_is_a_no_op()
    {
        var outer = new EditContext(new Model());
        var inner = new EditContext(new Model());

        using (EditContextScope.Push(outer))
        {
            var popper = EditContextScope.Push(inner);
            popper.Dispose();
            popper.Dispose();
            Assert.Same(outer, EditContextScope.Current);
        }
    }

    private sealed class Model
    {
        public string Name { get; set; } = "";
    }
}
