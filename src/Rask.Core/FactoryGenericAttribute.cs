namespace Rask.Core;

[AttributeUsage(AttributeTargets.Class)]
public sealed class FactoryGenericAttribute : Attribute
{
    public FactoryGenericAttribute(string typeParameter) => TypeParameter = typeParameter;

    public string TypeParameter { get; }

    // The non-generic property that the type parameter narrows. Required.
    public string ModelProperty { get; init; } = "";

    // Properties typed as `Delegate?` (or `object?`) on the underlying class that get a
    // type-narrowed Action<TModel>?/Func<TModel,Task>? pair in the generic overload. For
    // each name "X", the generic overload exposes `Action<TModel>? X` and a synthesized
    // `Func<TModel, Task>? XAsync` parameter; the body collapses both back into a single
    // Delegate? passed to the non-generic factory's X parameter.
    public string[] TypedDelegateProperties { get; init; } = Array.Empty<string>();

    // The constraint clause emitted as `where TModel : {Constraint}`. Defaults to `class`.
    public string Constraint { get; init; } = "class";
}
