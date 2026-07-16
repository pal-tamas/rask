using Rask.Core;
using Rask.Core.Forms;

namespace Rask.Testing;

// Renders nothing and hands the test the EditContext a surrounding form pushed onto EditContextScope.
//
// Deliberately internal, reached through RaskTest.EditContextProbe: a public component type would force
// consumers to write `new EditContextProbe(...)`, and RASK014 ("components must be created via factory
// methods") is an *error* that fires on any `new` of a Component outside Rask.Core — including on types we
// ship. A factory method is what that rule asks for, so this is the shape that composes with our own
// analyzer instead of fighting it.
//
// It captures during Render, not at construction, because the context is ambient only while the form's
// subtree is rendering — which is also why it must sit INSIDE the form's children to see anything.
internal sealed class EditContextProbe(Action<EditContext> capture) : Component
{
    protected override Component? Render()
    {
        if (EditContextScope.Current is { } context)
        {
            capture(context);
        }

        return null;
    }
}
