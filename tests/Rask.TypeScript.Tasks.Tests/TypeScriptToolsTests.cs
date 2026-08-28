using System.Runtime.InteropServices;

namespace Rask.TypeScript.Tasks.Tests;

/// <summary>
///     The pure half of the resolver: which package a machine needs, where it comes from, and where
///     the executable sits inside it.
/// </summary>
/// <remarks>
///     Every one of these is a fact about somebody else's packaging, and each is the kind of fact that
///     is right on the machine it was written on and wrong on a colleague's. That is the whole reason
///     they are pinned here rather than left to the first person whose build 404s.
/// </remarks>
public class TypeScriptToolsTests
{
    [Theory]
    [InlineData(TypeScriptTool.Esbuild, ToolOs.MacOs, Architecture.Arm64, "@esbuild/darwin-arm64")]
    [InlineData(TypeScriptTool.Esbuild, ToolOs.MacOs, Architecture.X64, "@esbuild/darwin-x64")]
    [InlineData(TypeScriptTool.Esbuild, ToolOs.Linux, Architecture.X64, "@esbuild/linux-x64")]
    [InlineData(TypeScriptTool.Esbuild, ToolOs.Linux, Architecture.Arm64, "@esbuild/linux-arm64")]
    [InlineData(TypeScriptTool.Esbuild, ToolOs.Windows, Architecture.X64, "@esbuild/win32-x64")]
    [InlineData(
        TypeScriptTool.Tsgo,
        ToolOs.MacOs,
        Architecture.Arm64,
        "@typescript/native-preview-darwin-arm64")]
    [InlineData(
        TypeScriptTool.Tsgo,
        ToolOs.Windows,
        Architecture.X64,
        "@typescript/native-preview-win32-x64")]
    public void PackageName_NamesThePublishedPackage(
        TypeScriptTool tool,
        ToolOs os,
        Architecture architecture,
        string expected) =>
        Assert.Equal(expected, TypeScriptTools.PackageName(tool, os, architecture));

    /// <summary>
    ///     Windows on ARM gets its own native build rather than an emulated x64 one.
    /// </summary>
    /// <remarks>
    ///     Worth its own test because the sibling Tailwind resolver deliberately does the opposite —
    ///     Tailwind publishes no windows-arm64 asset, so it hands those machines x64 and lets Windows
    ///     emulate. Copying that reasoning here would leave a native build unused.
    /// </remarks>
    [Fact]
    public void PackageName_WindowsOnArm_GetsANativeBuild()
    {
        Assert.Equal(
            "@esbuild/win32-arm64",
            TypeScriptTools.PackageName(TypeScriptTool.Esbuild, ToolOs.Windows, Architecture.Arm64));
        Assert.Equal(
            "@typescript/native-preview-win32-arm64",
            TypeScriptTools.PackageName(TypeScriptTool.Tsgo, ToolOs.Windows, Architecture.Arm64));
    }

    /// <summary>
    ///     esbuild publishes 32-bit builds and tsgo does not, so a 32-bit machine can bundle but not
    ///     type-check.
    /// </summary>
    /// <remarks>
    ///     Pinned because the tempting simplification — one platform table shared by both tools — would
    ///     hand a 32-bit Raspberry Pi a tsgo URL that 404s several minutes into its first build.
    /// </remarks>
    [Theory]
    [InlineData(Architecture.X86, "@esbuild/linux-ia32")]
    [InlineData(Architecture.Arm, "@esbuild/linux-arm")]
    public void PackageName_Esbuild_CoversThirtyTwoBit(Architecture architecture, string expected)
    {
        Assert.Equal(expected, TypeScriptTools.PackageName(TypeScriptTool.Esbuild, ToolOs.Linux, architecture));
        Assert.Null(TypeScriptTools.PackageName(TypeScriptTool.Tsgo, ToolOs.Linux, architecture));
    }

    /// <summary>
    ///     There is no musl variant, and that is deliberate rather than forgotten.
    /// </summary>
    /// <remarks>
    ///     The Tailwind resolver needs <c>-musl</c> assets and a loader probe to choose between them.
    ///     Both of these tools are statically linked Go, so the linux build runs on Alpine unchanged.
    ///     This test exists so that nobody "fixes the missing musl support" by adding a package name
    ///     that has never been published.
    /// </remarks>
    [Fact]
    public void PackageName_Linux_HasNoMuslVariant()
    {
        var name = TypeScriptTools.PackageName(TypeScriptTool.Esbuild, ToolOs.Linux, Architecture.X64);
        Assert.Equal("@esbuild/linux-x64", name);
        Assert.DoesNotContain("musl", name, StringComparison.Ordinal);
    }

    /// <summary>
    ///     The four executable layouts, which are all different from each other.
    /// </summary>
    /// <remarks>
    ///     esbuild disagrees with itself across platforms: <c>bin/esbuild</c> on Unix, but
    ///     <c>esbuild.exe</c> at the package root on Windows, with no <c>bin</c> directory at all.
    ///     Assuming one layout works on the machine it was written on and fails on somebody else's.
    /// </remarks>
    [Theory]
    [InlineData(TypeScriptTool.Esbuild, ToolOs.MacOs, "bin/esbuild")]
    [InlineData(TypeScriptTool.Esbuild, ToolOs.Linux, "bin/esbuild")]
    [InlineData(TypeScriptTool.Esbuild, ToolOs.Windows, "esbuild.exe")]
    [InlineData(TypeScriptTool.Tsgo, ToolOs.MacOs, "lib/tsgo")]
    [InlineData(TypeScriptTool.Tsgo, ToolOs.Windows, "lib/tsgo.exe")]
    public void ExecutablePath_MatchesThePublishedLayout(TypeScriptTool tool, ToolOs os, string expected) =>
        Assert.Equal(
            expected.Replace('/', Path.DirectorySeparatorChar),
            TypeScriptTools.ExecutablePath(tool, os));

    /// <summary>
    ///     tsgo needs its whole package; esbuild is genuinely one file.
    /// </summary>
    /// <remarks>
    ///     tsgo resolves <c>lib.dom.d.ts</c> and its ~110 siblings relative to its own location, so
    ///     extracting only the binary yields a compiler that reports every DOM type as undefined —
    ///     thousands of errors that look like the project is wrong rather than the install.
    /// </remarks>
    [Fact]
    public void NeedsWholePackage_IsTrueForTheCompilerOnly()
    {
        Assert.True(TypeScriptTools.NeedsWholePackage(TypeScriptTool.Tsgo));
        Assert.False(TypeScriptTools.NeedsWholePackage(TypeScriptTool.Esbuild));
    }

    /// <summary>
    ///     The tarball path keeps the scope but the filename drops it.
    /// </summary>
    /// <remarks>
    ///     This asymmetry is npm's, not ours, and getting it wrong is a 404 rather than an error that
    ///     names the mistake.
    /// </remarks>
    [Theory]
    [InlineData(
        "@esbuild/darwin-arm64",
        "0.28.2",
        "https://registry.npmjs.org/@esbuild/darwin-arm64/-/darwin-arm64-0.28.2.tgz")]
    [InlineData(
        "@typescript/native-preview-win32-x64",
        "7.0.0-dev.20260707.2",
        "https://registry.npmjs.org/@typescript/native-preview-win32-x64/-/native-preview-win32-x64-7.0.0-dev.20260707.2.tgz")]
    public void TarballUrl_DropsTheScopeFromTheFilenameOnly(string package, string version, string expected) =>
        Assert.Equal(expected, TypeScriptTools.TarballUrl(TypeScriptTools.DefaultRegistry, package, version));

    /// <summary>The metadata document percent-encodes the scope separator; the tarball path does not.</summary>
    [Fact]
    public void VersionDocumentUrl_EncodesTheScopeSeparator() =>
        Assert.Equal(
            "https://registry.npmjs.org/@esbuild%2fdarwin-arm64/0.28.2",
            TypeScriptTools.VersionDocumentUrl(TypeScriptTools.DefaultRegistry, "@esbuild/darwin-arm64", "0.28.2"));

    /// <summary>A trailing slash on a mirror URL must not produce a double slash.</summary>
    [Fact]
    public void TarballUrl_ToleratesATrailingSlashOnTheRegistry() =>
        Assert.Equal(
            "https://mirror.example/@esbuild/linux-x64/-/linux-x64-0.28.2.tgz",
            TypeScriptTools.TarballUrl("https://mirror.example/", "@esbuild/linux-x64", "0.28.2"));

    [Fact]
    public void ExpectedIntegrity_ReadsTheSha512()
    {
        const string Document =
            """{"dist":{"shasum":"f83afeeac1d7dac01c7a2fd012b3e451a0591fcc","integrity":"sha512-n4KqkOQ==","fileCount":3}}""";

        Assert.Equal("sha512-n4KqkOQ==", TypeScriptTools.ExpectedIntegrity(Document));
    }

    /// <summary>
    ///     A document offering only the legacy SHA-1 is treated as unverifiable, not as verified.
    /// </summary>
    /// <remarks>
    ///     The registry still publishes <c>shasum</c>, which is SHA-1 and no longer collision
    ///     resistant. Quietly falling back to it would leave the build reporting a verified download
    ///     while checking something that can be forged — the worst of both.
    /// </remarks>
    [Fact]
    public void ExpectedIntegrity_RefusesASha1OnlyDocument()
    {
        const string Document = """{"dist":{"shasum":"f83afeeac1d7dac01c7a2fd012b3e451a0591fcc"}}""";

        Assert.Null(TypeScriptTools.ExpectedIntegrity(Document));
    }

    /// <summary>An integrity that is present but not SHA-512 is refused too.</summary>
    [Fact]
    public void ExpectedIntegrity_RefusesANonSha512Algorithm() =>
        Assert.Null(TypeScriptTools.ExpectedIntegrity("""{"dist":{"integrity":"sha1-abcdef"}}"""));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not json at all")]
    [InlineData("""{"dist":{}}""")]
    public void ExpectedIntegrity_IsNullWhenThereIsNothingToRead(string? document) =>
        Assert.Null(TypeScriptTools.ExpectedIntegrity(document));

    /// <summary>
    ///     The cache is keyed by tool, version and platform, so nothing is ever overwritten in place.
    /// </summary>
    /// <remarks>
    ///     Keyed rather than flat because an upgrade must be a fresh directory: overwriting would mean
    ///     deleting a file another concurrent build may be executing at that moment.
    /// </remarks>
    [Fact]
    public void CacheDirectory_KeysByToolVersionAndPlatform()
    {
        var directory = TypeScriptTools.CacheDirectory(
            Path.Combine("root"),
            TypeScriptTool.Esbuild,
            "0.28.2",
            "@esbuild/darwin-arm64");

        Assert.Equal(Path.Combine("root", "esbuild", "0.28.2", "esbuild-darwin-arm64"), directory);
    }

    /// <summary>The cache lives beside the user's other Rask tooling, not inside a project.</summary>
    [Fact]
    public void DefaultCacheRoot_SitsBesideTheOtherRaskTooling() =>
        Assert.Equal(
            Path.Combine("home", ".rask", "typescript"),
            TypeScriptTools.DefaultCacheRoot("home"));
}
