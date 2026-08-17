namespace Rask.Core.Forms;

// The two answers to "where does this control's value come from", as types. They are the TMode argument
// of Build<T, TMode>: a form control's entry step fixes one of them, and each mode's steps are declared
// only on their own mode, so taking a step from the other is a compile error naming the mode the chain
// is actually in. Nothing is ever constructed — they exist only in the type.
//
// A type argument is the only thing an extension method can discriminate on, so these have to be types
// rather than an enum or a bool. CLOSED ones: sealed so nothing can derive a third mode that matches
// neither mode's steps, and a private constructor so none can be built. Nothing is ever constructed —
// they exist only in the type — and saying so in the declaration is cheap now and breaking later.

/// <summary>
///     The mode of a form-control chain that opened with <c>Bind</c>: the control two-way binds an
///     expression and drives the ambient <see cref="EditContext" />.
/// </summary>
/// <remarks>
///     Reached by <c>Control.Bind(() =&gt; model.Field)</c>. Adds <c>Validate</c>/<c>ValidateAsync</c> and
///     the post-bind hooks <c>AfterBind</c>/<c>AfterBindAsync</c>. The model owns the value and the
///     framework owns the write-back, so the <see cref="Controlled" /> steps are not offered.
/// </remarks>
public sealed class Bound
{
    private Bound()
    {
    }
}

/// <summary>
///     The mode of a form-control chain that opened with <c>Value</c> (or <c>Of</c>): the parent owns the
///     value and is notified of changes.
/// </summary>
/// <remarks>
///     Reached by <c>Control.Value(v)</c>, or by <c>Control.Of&lt;T&gt;()</c> for a control given no value
///     at all. Adds <c>Checked</c> and the change/input callbacks. Nothing parses a bind expression, so
///     the <see cref="Bound" /> steps are not offered.
/// </remarks>
public sealed class Controlled
{
    private Controlled()
    {
    }
}
