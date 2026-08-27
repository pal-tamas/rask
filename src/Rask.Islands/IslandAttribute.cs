namespace Rask.Islands;

/// <summary>
///     Marks a component whose markup is produced by another framework — a <c>.tsx</c>, <c>.jsx</c> or
///     Lit component sitting beside it.
/// </summary>
/// <remarks>
///     <para>
///         An island is an <b>ordinary component</b>, not a subclass of some island base type. That is
///         deliberate and it is what makes "every component is replaceable" true rather than
///         aspirational: a base class would consume the single-inheritance slot, so a component already
///         extending <c>BsBlock</c> or an app's own base could never become island-backed. Declaring it
///         with an attribute also matches how the rest of the framework declares behaviour —
///         <c>[Route]</c>, <c>[ParentRoute]</c>, <c>[LocalOnly]</c> — rather than making this the one
///         feature that works by inheritance.
///     </para>
///     <para>
///         Migration is therefore an attribute and a deletion: add <c>[Island]</c>, remove
///         <c>Render()</c>, and the component is now React-backed with the same props and the same call
///         sites.
///     </para>
///     <para>
///         The front-end file is found the way scoped CSS and scoped JS already are — by filename,
///         beside the class. <c>Chart.cs</c> pairs with <c>Chart.tsx</c>. Name <see cref="Module" />
///         only when the component lives somewhere that convention cannot reach, such as a shared
///         design system on npm.
///     </para>
///     <example>
///         <code>
///         [Island]
///         public sealed partial class Chart : Component
///         {
///             public required IReadOnlyList&lt;Point&gt; Series { get; set; }
///             public string? Title { get; set; }
///         }
///         </code>
///     </example>
/// </remarks>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class IslandAttribute : Attribute
{
    /// <summary>Pairs the component with the sibling front-end file that shares its name.</summary>
    public IslandAttribute()
    {
    }

    /// <summary>Pairs the component with an explicit module rather than a sibling file.</summary>
    /// <param name="module">
    ///     What the browser imports: a path relative to the component, or a bare specifier resolved by
    ///     the bundler (<c>"@acme/charts/Chart"</c>).
    /// </param>
    public IslandAttribute(string module) => Module = module;

    /// <summary>
    ///     The module the adapter imports, or <see langword="null" /> to use the sibling file.
    /// </summary>
    public string? Module { get; }

    /// <summary>
    ///     Which adapter renders this. Left at <see cref="IslandRuntime.Infer" /> it is read from the
    ///     module's extension.
    /// </summary>
    /// <remarks>
    ///     Lit has to say so explicitly, because a Lit component is an ordinary <c>.ts</c> file and
    ///     nothing about the extension distinguishes it from any other TypeScript in the project.
    ///     <c>.tsx</c> and <c>.jsx</c> infer as <see cref="IslandRuntime.React" /> — which also covers
    ///     Preact, since a Preact project aliases <c>react</c> to <c>preact/compat</c> and the adapter
    ///     compiles unchanged against it.
    /// </remarks>
    public IslandRuntime Runtime { get; init; } = IslandRuntime.Infer;
}
