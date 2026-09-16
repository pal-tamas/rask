namespace Rask.Core;

/// <summary>
/// Puts this component's chain openings on ANOTHER component's entry, so both are reached by one name.
/// </summary>
/// <remarks>
/// <para>
/// A chain normally starts at an entry named after the component, and two components cannot share one name
/// (RASK040). Sometimes they should: a select over one value and a select over a collection of them are the same
/// control to the person writing the page, and the model already says which it is — so
/// <c>UiSelect.Bind(() =&gt; model.Country)</c> and <c>UiSelect.Bind(() =&gt; model.Tags)</c> both read as "a select",
/// while the C# type each binds keeps them apart.
/// </para>
/// <para>
/// The entry is the ONLY thing shared. Each component keeps its own states, its own required steps and its own
/// setters, so the openings are told apart by the types they take — which means they must differ there, or the
/// call is ambiguous. The component named here must exist in the same compilation.
/// </para>
/// </remarks>
/// <param name="entry">The entry name to join — the other component's type name, without generic arity.</param>
[AttributeUsage(AttributeTargets.Class)]
public sealed class RaskChainEntryAttribute(string entry) : Attribute
{
    /// <summary>The entry name this component's openings are added to.</summary>
    public string Entry { get; } = entry;
}
