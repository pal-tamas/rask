namespace Rask.Generators.Shared;

/// <summary>A collection of children keyed by several columns, and how the holder's form carries it.</summary>
internal sealed class ModelCompositeChild(ModelChild child, CompositeChildForm form)
{
    /// <summary>The collection.</summary>
    public ModelChild Child { get; } = child;

    /// <summary>A list of ids or of rows on the form — or why there is neither.</summary>
    public CompositeChildForm Form { get; } = form;
}
