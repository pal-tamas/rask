namespace Rask.External;

/// <summary>
///     A Rask component rendered by React, from the <c>.tsx</c> file beside it.
/// </summary>
/// <remarks>
///     <para>
///         Still covers Preact through <c>preact/compat</c> aliasing, for a project already built that
///         way: aliasing <c>react</c> and <c>react-dom</c> in both tsconfig and the Vite plugin is what
///         the TypeScript SPA lane relies on, and this adapter cannot tell the difference. New code
///         should reach for <see cref="PreactComponent" /> instead, which imports Preact directly and
///         needs no aliasing to be right.
///     </para>
///     <para>
///         The front-end file stays an ordinary React component with no Rask import in it: the build
///         generates an entry module that pairs it with the adapter, so the <c>.tsx</c> remains
///         portable and testable on its own.
///     </para>
/// </remarks>
/// <example>
///     <code>
///     public sealed partial class Chart : ReactComponent
///     {
///         public required IReadOnlyList&lt;Point&gt; Series { get; set; }
///         public Action&lt;int&gt;? OnPointClick { get; set; }
///     }
///     </code>
/// </example>
public abstract partial class ReactComponent : ExternalComponent
{
    /// <inheritdoc />
    protected sealed override string Runtime => "react";
}
