namespace Rask.External;

/// <summary>
///     A Rask component rendered by Angular, from the <c>.ts</c> file beside it.
/// </summary>
/// <remarks>
///     <para>
///         A standalone component, bootstrapped imperatively: the adapter calls
///         <c>createApplication()</c>, then <c>appRef.bootstrap(component, hostElement)</c>, and drives
///         updates through <c>ComponentRef.setInput</c>. So an update is a change-detection pass over
///         the component that is already there rather than a new application, and the island keeps its
///         own state across a C# re-render like every other runtime here.
///     </para>
///     <para>
///         <strong>Bootstrapping is asynchronous</strong> — <c>createApplication()</c> returns a
///         promise, and it is the only runtime here that does. Props that arrive before it resolves
///         are held and applied on arrival rather than dropped, and an island unmounted while still
///         booting destroys the application when it appears instead of leaking it.
///     </para>
///     <para>
///         <c>@Input()</c> or <c>input()</c> is what <c>setInput</c> can reach. A plain public field is
///         not an input, and Angular says so in dev builds while a production build ignores it
///         silently — so a prop that never arrives is the first thing to check.
///     </para>
///     <para>
///         Angular's is the heaviest runtime of the set: a one-component island bundles to roughly
///         73 kB gzipped, against 12 kB for Preact and 10 kB for Solid. Worth knowing before putting
///         one on a landing page.
///     </para>
///     <para>
///         The build needs more than the Vite plugin — <c>@angular/compiler-cli</c> and
///         <c>@angular/build</c> at the same major, and a TypeScript under 6.1, which
///         <c>@angular/compiler-cli</c> pins. <c>docs/islands.md</c> lists the set.
///     </para>
/// </remarks>
/// <example>
///     <code>
///     // Chart.cs
///     public sealed partial class Chart : AngularComponent
///     {
///         public required IReadOnlyList&lt;Point&gt; Series { get; set; }
///         public Action&lt;int&gt;? OnPointClick { get; set; }
///     }
///
///     // Chart.ts
///     import { Component, Input } from '@angular/core'
///
///     @Component({ selector: 'app-chart', standalone: true, template: `…` })
///     export class Chart {
///         @Input() series: Point[] = []
///         @Input() onPointClick?: (index: number) =&gt; void
///     }
///
///     export default Chart
///     </code>
/// </example>
public abstract partial class AngularComponent : ExternalComponent
{
    /// <inheritdoc />
    protected sealed override string Runtime => "angular";
}
