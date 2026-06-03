using Rask.Core.Forms;

namespace Rask.Core.Tests.Forms;

public class EditContextScopeTests
{
    [Fact]
    public void Current_DefaultsNull() => Assert.Null(EditContextScope.Current);

    [Fact]
    public void Push_SetsCurrent_DisposeRestoresPrev()
    {
        var ctx = new EditContext(new Model());

        using (EditContextScope.Push(ctx))
        {
            Assert.Same(ctx, EditContextScope.Current);
        }

        Assert.Null(EditContextScope.Current);
    }

    [Fact]
    public void Push_NestedScopes_RestoreInLifoOrder()
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
    public void Push_DoubleDispose_NoOpAfterFirstDispose()
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
