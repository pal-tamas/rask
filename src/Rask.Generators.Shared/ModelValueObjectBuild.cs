namespace Rask.Generators.Shared;

/// <summary>How generated code rebuilds a value object from its nested model, best first.</summary>
internal enum ModelValueObjectBuild
{
    /// <summary>A PUBLIC constructor naming every property — a positional record: <c>new Money(amount, currency)</c>.</summary>
    Constructor,

    /// <summary>A PUBLIC parameterless constructor and a public setter on every property: <c>new Money { Amount = … }</c>.</summary>
    Initializer,

    /// <summary>A non-public constructor naming every property, called through <c>[UnsafeAccessor]</c>.</summary>
    AccessorConstructor,

    /// <summary>
    ///     A parameterless constructor of any accessibility, then every property written: a public setter
    ///     directly, a non-public setter or a backing field through <c>[UnsafeAccessor]</c>.
    /// </summary>
    AccessorMembers,

    /// <summary>
    ///     Declared from a base record (<c>record DriverName(string Value) : Text(Value, MaxLength: 1000)</c>): built through the generated
    ///     <c>DriverName.Create(value)</c>, never a constructor.
    /// </summary>
    Factory,
}
