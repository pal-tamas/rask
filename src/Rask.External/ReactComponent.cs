namespace Rask.External;

/// <summary>
///     A Rask component rendered by React, from the <c>.tsx</c> file beside it.
/// </summary>
/// <remarks>
///     <para>
///         Covers Preact unchanged. A Preact project aliases <c>react</c> and <c>react-dom</c> to
///         <c>preact/compat</c> in both tsconfig and the Vite plugin — the same aliasing the TypeScript
///         SPA lane already relies on — so one adapter serves both and Rask never needs to know which
///         it got.
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
