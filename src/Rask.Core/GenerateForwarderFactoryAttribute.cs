namespace Rask.Core;

// Marks a `public static` method on a Component subclass as the body for a generator-emitted
// factory forwarder. ComponentFactoryGenerator captures the method's signature (generics,
// constraints, parameters with defaults, params modifiers) and emits a public method in the
// `{Namespace}.Components` partial with the same shape, named after the declaring class,
// that one-line-delegates to the source method. Use this when a factory body needs runtime
// logic (expression parsing, conditional dispatch, handler composition) that can't be derived
// from class metadata alone — e.g. the Expression-driven `Input<TProp>(Bind: ...)` factory.
[AttributeUsage(AttributeTargets.Method)]
public sealed class GenerateForwarderFactoryAttribute : Attribute
{
    // When set to the name of a `System.Delegate?` parameter on the source method, the generator fans the
    // forwarder into three overloads (none / sync / async) instead of one verbatim forwarder — the
    // validator parameter is omitted (forwarded as null), typed `Validate<T>`, or typed `ValidateAsync<T>`
    // respectively, where T is the method's first type parameter. Lets a control declare a single `Bound`
    // core and get the cast-free Validate overloads generated, instead of hand-writing all three.
    public string? Validator { get; init; }
}
