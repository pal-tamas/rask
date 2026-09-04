namespace Rask.Core.Forms;

// The capability behind the submit-state children indexer, as an interface rather than a check the
// indexer performs. FormBuild<T> constrains T to this, so the only chains that OFFER the indexer are
// the ones that can actually answer it — the same "not offered" rule the two chain modes follow, one
// level up: there, a step from the wrong mode is unreachable; here, the whole indexer is.
//
// It carries a method rather than a settable property because the chain hands the factory DOWN into a
// component it holds only as T. A property would need the same interface anyway, and would also read as
// something a component author sets, which nothing does — the indexer is its only caller.

/// <summary>
///     A component whose children may be given as a function of whether a submit is in flight, rather
///     than as a fixed list.
/// </summary>
/// <remarks>
///     Implemented by <c>Form&lt;TModel&gt;</c>. Supplying the factory is what
///     <c>Form.Model(model)[submitting =&gt; [ … ]]</c> does; the component invokes it on every render,
///     so the children it returns follow the submit state without the page holding that state itself.
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
