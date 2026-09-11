using System;
using Microsoft.CodeAnalysis;

namespace Rask.Generators.External;

/// <summary>
///     The island runtimes, as the generators need to see them: which base class declares one, and which
///     file extension its module is inferred to have.
/// </summary>
/// <remarks>
///     <para>
///         A table rather than a chain of <c>if</c>s. Two runtimes fit in a ternary; seven do not, and the
///         failure of the ternary is silent — a Vue component would infer <c>./Chart.tsx</c> and the browser
///         would resolve a module the bundle never built.
///     </para>
///     <para>
///         The extension mirrors the discovery globs in <c>Rask.External.targets</c>, but it no longer
///         <em>decides</em> the runtime there: React, Preact and Solid all claim <c>.tsx</c>, and Angular
///         joins Lit on <c>.ts</c>. So this table is the authority for both halves — carried out of the
///         compilation as constants, and read back by the build.
///     </para>
///     <para>
///         Shared between <c>ExternalGenerator</c> and the factory generator's package-island step, which
///         both have to agree on what an island is.
///     </para>
/// </remarks>
internal static class ExternalRuntimes
{
    public static readonly (string BaseName, string Runtime, string Extension)[] All =
    {
        ("Rask.External.ReactComponent", "react", "tsx"),
        ("Rask.External.PreactComponent", "preact", "tsx"),
        ("Rask.External.SolidComponent", "solid", "tsx"),
        ("Rask.External.LitComponent", "lit", "ts"),
        ("Rask.External.AngularComponent", "angular", "ts"),
        ("Rask.External.VueComponent", "vue", "vue"),
        ("Rask.External.SvelteComponent", "svelte", "svelte"),
    };

    /// <summary>The runtime <paramref name="type" /> inherits, or null when it is not an island.</summary>
    /// <remarks>
    ///     Matched by name rather than symbol identity, because the factory generator asks while building a
    ///     candidate, where no resolved base symbol is at hand. The bases are non-generic, so the display
    ///     name is exact.
    /// </remarks>
    public static string? RuntimeOf(INamedTypeSymbol type)
    {
        for (var t = type.BaseType; t is not null; t = t.BaseType)
        {
            var name = t.OriginalDefinition.ToDisplayString();
            foreach (var (baseName, runtime, _) in All)
            {
                if (string.Equals(name, baseName, StringComparison.Ordinal))
                {
                    return runtime;
                }
            }
        }

        return null;
    }

    /// <summary>
    ///     The child type a runtime's islands take — <c>Rask.External.ReactChild</c> for React — with its base class and
    ///     the name messages call the runtime by, or null for a runtime the table does not know.
    /// </summary>
    /// <remarks>
    ///     Derived from the base name rather than tabled a second time: the child type sits beside its base class and is
    ///     named after it, so the two cannot drift.
    /// </remarks>
    public static (string BaseName, string ChildName, string Label)? ChildTypeOf(string runtime)
    {
        foreach (var (baseName, key, _) in All)
        {
            if (string.Equals(key, runtime, StringComparison.Ordinal))
            {
                var stem = baseName.Substring(0, baseName.Length - "Component".Length);
                return (baseName, stem + "Child", stem.Substring(stem.LastIndexOf('.') + 1));
            }
        }

        return null;
    }

    /// <summary>The sibling file's extension for a runtime, without the dot.</summary>
    /// <remarks>
    ///     Falls back to <c>tsx</c> for a runtime the table does not know, which cannot happen: the key came
    ///     from the table in the first place. Stated rather than thrown because a generator that throws takes
    ///     the whole compilation down.
    /// </remarks>
    public static string Extension(string runtime)
    {
        foreach (var (_, key, extension) in All)
        {
            if (string.Equals(key, runtime, StringComparison.Ordinal))
            {
                return extension;
            }
        }

        return "tsx";
    }
}
