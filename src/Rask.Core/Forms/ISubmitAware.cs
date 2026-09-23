namespace Rask.Core.Forms;

// VESTIGIAL. This was the capability behind the submit-state children indexer while that indexer lived
// on a chain type of its own (FormBuild<T>, constrained to this interface). The chain receives on the
// component now, so the indexer is declared on Form<TModel> itself (see Form's `this[Func<bool, …>]`),
// and nothing in the framework implements or consumes this interface any more — it survives only as
// recorded public surface.

/// <summary>
///     A component whose children may be given as a function of whether a submit is in flight, rather
///     than as a fixed list.
/// </summary>
/// <remarks>
///     No longer implemented by <c>Form&lt;TModel&gt;</c>, which declares its submit-state indexer
///     (<c>Form.Model(model)[submitting =&gt; [ … ]]</c>) on itself; nothing in the framework consumes this
///     interface any more.
/// </remarks>
public interface ISubmitAware
{
    /// <summary>
    ///     Gives the component a children factory, replacing any fixed children it was given before.
    /// </summary>
    /// <param name="factory">
    ///     Called on every render with <c>true</c> while a submit is in flight. It runs inside the render
    ///     walk, so the components it builds keep their identity across renders.
    /// </param>
    void SetChildrenFactory(Func<bool, IEnumerable<Component?>> factory);
}
