namespace Rask.Core;

/// <summary>
///     Base class for a routable component — a page. Deriving from <see cref="Page" /> is what makes a
///     component reachable by URL; the <see cref="Route" /> override declares the URL it answers.
///     <code>
/// public sealed class ProductPage : Page
/// {
///     protected override string Route => "/products/{id:int}";
///
///     public int Id { get; set; }   // bound from the {id} segment
/// }
///     </code>
///     <para>
///         <see cref="Route" /> must be a <b>compile-time constant</b> — a literal, a <c>const</c>, or a
///         constant expression. The route table is built at compile time (that is what makes
///         <c>ProductPage.Url(42)</c> and <c>ProductPage.Go(42)</c> type-safe), so a value that could only be
///         known at run time is <b>RASK036</b>, not a route that quietly never registers.
///     </para>
///     <para>
///         A page renders the content <i>inside</i> the app shell, not the shell itself — the root component
///         owns <c>Doctype</c>/<c>Html</c>/<c>Head</c>/<c>Body</c> and hosts a <c>Router()</c>. For a page
///         nested under another page's <c>Outlet()</c>, override <see cref="Parent" />.
///     </para>
/// </summary>
public abstract class Page : Component
{
    /// <summary>
    ///     The URL template this page answers, e.g. <c>"/"</c>, <c>"/products"</c> or
    ///     <c>"/products/{id:int}"</c>. Each <c>{segment}</c> binds to the public settable property of the same
    ///     name. Must be a compile-time constant (see <b>RASK036</b>).
    ///     <para>
    ///         When <see cref="Parent" /> is set, this template is <i>relative</i> to the parent's and is
    ///         composed onto it.
    ///     </para>
    /// </summary>
    protected abstract string Route { get; }

    /// <summary>
    ///     The page this one is nested under, or <c>null</c> (the default) for a top-level page. A nested page
    ///     renders into its parent's <c>Outlet()</c>, and its <see cref="Route" /> is composed onto the
    ///     parent's — so a parent of <c>"/app"</c> and a child of <c>"settings"</c> answers
    ///     <c>/app/settings</c>. Must be a <c>typeof(...)</c> of another <see cref="Page" />.
    /// </summary>
    protected virtual Type? Parent => null;
}
