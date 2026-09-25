namespace Rask.Generators.Blazor;

/// <summary>
///     One hosted <c>[Parameter]</c>, as both a chain step and a dictionary entry.
/// </summary>
/// <param name="Parameter">The hosted component's own parameter name — the dictionary key.</param>
/// <param name="Name">What the island calls it, which is the chain step's name.</param>
/// <param name="ChainTypeFqn">The generated property's type.</param>
/// <param name="EventArg">For an <c>EventCallback&lt;T&gt;</c>, the fully-qualified <c>T</c>.</param>
/// <param name="IsEventCallback">Whether the hosted parameter is an <c>EventCallback</c>.</param>
/// <param name="IsRequired">
///     Whether the hosted component marked it <c>[EditorRequired]</c>, which makes it a required
///     chain step rather than an optional one.
/// </param>
/// <param name="NeedsNew">
///     Whether the property shadows an inherited chain entry and so must say <c>new</c>.
/// </param>
/// <param name="DeclaredByUser">
///     Whether the island already declares this property itself — via <c>[BlazorParameter]</c> or a
///     plain hand-written property. It is still WRITTEN to the parameter dictionary; it just must not
///     be declared a second time.
/// </param>
internal readonly record struct BlazorParam(
    string Parameter,
    string Name,
    string ChainTypeFqn,
    string? EventArg,
    bool IsEventCallback,
    bool IsRequired,
    bool NeedsNew,
    bool DeclaredByUser);
