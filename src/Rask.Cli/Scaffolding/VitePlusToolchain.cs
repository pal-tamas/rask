using Semver;

namespace Rask.Cli.Scaffolding;

/// <summary>
///     The Vite+ toolchain a front-end template installs: <c>vite-plus</c>, and <c>vite</c> aliased to the core it
///     ships, in <c>devDependencies</c> and again in <c>overrides</c> so that every plugin resolves the same one.
/// </summary>
/// <remarks>
///     <para>
///         The three spellings move together or not at all. <c>vite-plus</c> depends on its own core at one exact
///         version, and the override forces that core on everything that asks for <c>vite</c> — so a <c>vite-plus</c>
///         a caret let npm move on would run beside a core the override held back. Hence an exact pin, unlike every
///         other package <see cref="FrontEndVersions" /> chooses, and the alias written to the same version.
///     </para>
///     <para>
///         The plugins never see <c>vite-plus</c>: they declare a peer range on <c>vite</c>, and what answers under
///         that name is the Vite the core bundles (<see cref="NpmRegistry.BundledViteAsync" />). So that is the
///         version they are held to, exactly as they were held to <c>vite</c> itself before the toolchain replaced it.
///     </para>
/// </remarks>
internal static class VitePlusToolchain
{
    /// <summary>The package the templates name, and the one whose version the other two follow.</summary>
    internal const string Package = "vite-plus";

    /// <summary>The name the plugins' peer ranges ask for, which the alias answers.</summary>
    internal const string Vite = "vite";

    /// <summary>The package <c>vite</c> is aliased to.</summary>
    internal const string Core = "@voidzero-dev/vite-plus-core";

    /// <summary>The alias as a template writes it, before a version is chosen.</summary>
    internal const string FloatingAlias = "npm:" + Core + "@" + FrontEndVersions.Latest;

    /// <summary>Whether <paramref name="manifest" /> installs the toolchain through the alias.</summary>
    internal static bool Uses(string manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);

        return manifest.Contains(FloatingAlias, StringComparison.Ordinal);
    }

    /// <summary><paramref name="manifest" /> with every floating alias written to <paramref name="version" />.</summary>
    internal static string Pin(string manifest, SemVersion version)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(version);

        return manifest.Replace(FloatingAlias, $"npm:{Core}@{version}", StringComparison.Ordinal);
    }
}
