namespace Rask.External;

/// <summary>
///     A Rask component rendered by a Lit element, from the <c>.ts</c> file beside it.
/// </summary>
/// <remarks>
///     <para>
///         Naming the runtime in the base class is what makes Lit workable at all. A Lit component is
///         an ordinary <c>.ts</c> file and nothing about the extension distinguishes it from any other
///         TypeScript in the project, so before this it had to be declared twice — once in C# and once
///         to the build. Here the type says it.
///     </para>
///     <para>
///         The module <strong>default-exports its registered tag name</strong>, because a custom
///         element registers its own tag and nothing else about the file reveals it. Importing the
///         module runs that registration as a side effect.
///     </para>
///     <para>
///         This works for any custom element with property-shaped inputs, not only Lit ones — the
///         adapter creates the element, assigns props as properties, and removes it on unmount.
///     </para>
/// </remarks>
/// <example>
///     <code>
///     // Gauge.cs
///     public sealed partial class Gauge : LitComponent
///     {
///         public double Value { get; set; }
///     }
///
///     // Gauge.ts
///     @customElement('app-gauge')
///     export class AppGauge extends LitElement { … }
///     export default 'app-gauge'
///     </code>
/// </example>
public abstract partial class LitComponent : ExternalComponent
{
    /// <inheritdoc />
    protected sealed override string Runtime => "lit";
}
