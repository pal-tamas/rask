using System;
using Rask.Generators.Shared;

namespace Rask.Generators.External.PackageIslands;

/// <summary>Reading a package module specifier.</summary>
internal static class PackageSpecifier
{
    /// <summary>
    ///     Whether <paramref name="module" /> names a package (<c>@mui/material/Button</c>) rather than a
    ///     file (<c>./Chart.tsx</c>), a root path, a Node subpath import (<c>#internal</c>) or a URL.
    /// </summary>
    public static bool IsBare(string module)
    {
        if (module.Length == 0)
        {
            return false;
        }

        var first = module[0];
        if (first is '.' or '/' or '\\' or '#')
        {
            return false;
        }

        if (module.IndexOf("://", StringComparison.Ordinal) >= 0 || (module.Length > 1 && module[1] == ':'))
        {
            return false;
        }

        return true;
    }

}
