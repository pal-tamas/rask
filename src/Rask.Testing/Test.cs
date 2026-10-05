// The probe is a root built from a runtime argument (the capture callback), not a chain a parent
// renders, so RASK014 does not apply to it.
#pragma warning disable RASK014

using Rask.Core;
using Rask.Core.Forms;

namespace Rask.Testing;

/// <summary>
///     What a test brings that is about the test rather than about the page: the batteries' fakes
///     (<c>Mail.Fake()</c>, <c>Clock.Fake(at)</c>), the route helpers, and the probe below. A
///     page itself comes from <see cref="Page.Visit" /> or <see cref="Page.Render(Func{Component}, IServiceProvider)" />.
/// </summary>
public static partial class Test
{
    /// <summary>
    ///     A zero-markup component that hands <paramref name="capture" /> the <see cref="EditContext" /> the
    ///     surrounding form is using, so a test can assert validation state (<c>GetValidationMessages</c>,
    ///     <c>IsValidating</c>, <c>IsModified</c>) that never reaches the markup. Place it <b>inside</b> the
    ///     form's children — the context is ambient only within that subtree:
    ///     <code>
    ///     EditContext? ctx = null;
    ///     var page = Page.Render(() => Form.Model(model)[
    ///         Input.Bind(() => model.Name),
    ///         Test.EditContextProbe(c => ctx = c)
    ///     ]);
    ///     </code>
    ///     The callback runs on every render, so <paramref name="capture" /> sees the current context.
    /// </summary>
    /// <param name="capture">Receives the ambient context during each render.</param>
    public static Component EditContextProbe(Action<EditContext> capture)
    {
        ArgumentNullException.ThrowIfNull(capture);
        return new EditContextProbe(capture);
    }
}
