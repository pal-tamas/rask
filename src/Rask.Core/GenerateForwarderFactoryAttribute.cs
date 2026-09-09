namespace Rask.Core;

// Marks a `public static` method on a Component subclass as the body for a generator-emitted
// factory forwarder. ComponentFactoryGenerator captures the method's signature (generics,
// constraints, parameters with defaults, params modifiers) and emits a public method in the
// `{Namespace}.Components` partial with the same shape, named after the declaring class,
// that one-line-delegates to the source method. Use this when a factory body needs runtime
// logic (expression parsing, conditional dispatch, handler composition) that can't be derived
// from class metadata alone — e.g. the Expression-driven `Input<TProp>(Bind: ...)` factory.
[AttributeUsage(AttributeTargets.Method)]
//
// It carried a `Validator` option once, which fanned the forwarder into none/sync/async overloads so a
// control could take either rule shape without a cast. `Validator<T>` does that now — one property, one
// step, an overload per shape — so the option had nothing left to do.
public sealed class GenerateForwarderFactoryAttribute : Attribute;
