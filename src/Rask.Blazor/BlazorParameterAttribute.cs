namespace Rask.Blazor;

/// <summary>
///     Names the hosted component's <c>[Parameter]</c> that this property feeds, when the two cannot
///     share a name.
/// </summary>
/// <remarks>
///     <para>
///         This is load-bearing rather than cosmetic. A chain entry is a member named after the
///         component, so a property called <c>Title</c>, <c>Style</c>, <c>Class</c>, <c>Label</c>,
///         <c>Data</c> or <c>Form</c> collides with the entry of the same name and fails with CS0108
///         under <c>-warnaserror</c> — the collision documented for islands in
///         <c>docs/islands.md</c>. Every real Blazor component library has parameters called
///         <c>Class</c> and <c>Style</c>, so without a rename this package would be unusable against
///         MudBlazor on the first day.
///     </para>
///     <para>
///         It is also the seam for a parameter whose Blazor name is simply not a good C# property
///         name at the call site: <c>ChartSeries</c> reads better as <c>Series</c> in a chain.
///     </para>
/// </remarks>
/// <example>
///     <code>
///     public sealed partial class Chart : BlazorComponent&lt;MudChart&gt;
///     {
///         [BlazorParameter("ChartSeries")]
///         public required List&lt;ChartSeries&gt; Series { get; set; }
///
///         [BlazorParameter("Class")]
///         public string? Appearance { get; set; }
///     }
///     </code>
/// </example>
[AttributeUsage(AttributeTargets.Property)]
public sealed class BlazorParameterAttribute : Attribute
{
    /// <param name="name">The hosted component's parameter name, spelled exactly as it declares it.</param>
    public BlazorParameterAttribute(string name) => Name = name;

    /// <summary>The hosted component's parameter name.</summary>
    public string Name { get; }
}
