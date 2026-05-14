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

    // Properties typed as `Delegate?` that hold a validator callback. For each name "X",
    // the generic overload exposes `Func<TModel, IEnumerable<string>>? X` (sync) and a
    // synthesized `Func<TModel, CancellationToken, ValueTask<IEnumerable<string>>>? XAsync`
    // sibling. Both collapse back into a single Delegate? for the non-generic factory.
    // Separate from TypedDelegateProperties because the validator shape returns messages
    // and supports cancellation — Action/Func<Task> doesn't.
    public string[] TypedValidatorProperties { get; init; } = Array.Empty<string>();

    // The constraint clause emitted as `where TModel : {Constraint}`. Defaults to `class`.
    public string Constraint { get; init; } = "class";
}
