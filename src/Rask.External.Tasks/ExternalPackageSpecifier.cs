using System;

namespace Rask.External.Tasks;

/// <summary>
///     Reads a package island's <c>Module</c> specifier on the build side: whether it names a package, which
///     export, and which package.
/// </summary>
/// <remarks>
///     The same rules as the island generator's <c>PackageSpecifier</c>, restated because this assembly is an
///     MSBuild task and cannot reference the analyzer. The two halves must agree — a specifier the generator
///     treats as a package and the build treats as a file would generate props for a snapshot nobody extracts
///     — so <c>ExternalPackageSpecifierTests</c> pins the same cases the generator's tests do.
/// </remarks>
internal static class ExternalPackageSpecifier
{
    /// <summary>
    ///     Whether <paramref name="module" /> names a package (<c>@mui/material/Button</c>) rather than a file
    ///     (<c>./Chart.tsx</c>), a root path, a Node subpath import (<c>#internal</c>) or a URL.
    /// </summary>
    public static bool IsBare(string module)
    {
        if (string.IsNullOrEmpty(module))
        {
            return false;
        }

        var first = module[0];
        if (first is '.' or '/' or '\\' or '#')
        {
            return false;
        }

        return module.IndexOf("://", StringComparison.Ordinal) < 0 && !(module.Length > 1 && module[1] == ':');
    }

    /// <summary>
    ///     The specifier and the export: <c>"@mui/material#Button"</c> is <c>(@mui/material, Button)</c>; a
    ///     specifier without a <c>#</c> names the default export.
    /// </summary>
    public static (string Specifier, string Export) Split(string module)
    {
        var hash = module.LastIndexOf('#');
        return hash > 0 && hash < module.Length - 1
            ? (module.Substring(0, hash), module.Substring(hash + 1))
            : (module, "default");
    }

    /// <summary>The package a specifier imports from: <c>@mui/material/Button</c> is <c>@mui/material</c>.</summary>
    public static string PackageName(string specifier)
    {
        var parts = specifier.Split('/');
        return specifier.StartsWith("@", StringComparison.Ordinal) && parts.Length > 1
            ? parts[0] + "/" + parts[1]
            : parts[0];
    }

    /// <summary>
    ///     Whether <paramref name="export" /> can be written into generated JavaScript — <c>default</c>, an
    ///     identifier, or a dotted path of identifiers (<c>Switch.Root</c>, a member of the <c>Switch</c> export) — so
    ///     nothing a <c>Module</c> string carries can end the import and start other code.
    /// </summary>
    public static bool IsValidExport(string export)
    {
        if (string.Equals(export, "default", StringComparison.Ordinal))
        {
            return true;
        }

        foreach (var segment in export.Split('.'))
        {
            if (!IsIdentifier(segment))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    ///     Whether <paramref name="export" /> can be written into generated JavaScript for an island of
    ///     <paramref name="runtime" />: anything <see cref="IsValidExport(string)" /> accepts, and — for Lit only — the tag
    ///     an element registers (<c>sl-switch</c>), since a module that only registers an element exports nothing to name.
    /// </summary>
    /// <remarks>
    ///     A Lit island never takes a member of an export (<c>Switch.Root</c>): its entry imports the module and mounts
    ///     an element by tag, so the member would be dropped and the island would mount whatever the tag names instead.
    /// </remarks>
    public static bool IsValidExport(string export, string runtime) =>
        string.Equals(runtime, "lit", StringComparison.Ordinal)
            ? IsTag(export) || (IsValidExport(export) && export.IndexOf('.') < 0)
            : IsValidExport(export);

    /// <summary>
    ///     Whether <paramref name="name" /> is a custom element name as Rask accepts one: a lowercase letter first, then
    ///     lowercase letters, digits, <c>.</c>, <c>_</c> and <c>-</c>, with at least one <c>-</c>.
    /// </summary>
    public static bool IsTag(string? name)
    {
        if (string.IsNullOrEmpty(name) || name![0] < 'a' || name[0] > 'z' || name.IndexOf('-') < 0)
        {
            return false;
        }

        foreach (var c in name)
        {
            if (!((c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c is '.' or '_' or '-'))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsIdentifier(string name)
    {
        if (name.Length == 0 || !(char.IsLetter(name[0]) || name[0] is '_' or '$'))
        {
            return false;
        }

        foreach (var c in name)
        {
            if (!(char.IsLetterOrDigit(c) || c is '_' or '$'))
            {
                return false;
            }
        }

        return true;
    }
}
