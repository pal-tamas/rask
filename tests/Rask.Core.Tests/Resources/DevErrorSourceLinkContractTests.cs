using System.Text.RegularExpressions;

namespace Rask.Core.Tests.Resources;

/// <summary>
///     Source-level contract for the dev error panel's source links: a stack frame or compiler error that
///     names a file on this machine opens it in VS Code at that line.
///     <para>
///         Structural, for the same reason as <see cref="BuildStatusClientContractTests" />: the panel lives in
///         a runtime that boots against a live document. What is pinned is what would fail silently or
///         dangerously — a stack rendered as HTML, a link built for a path that names no file here, or a
///         panel that stopped using the renderer at all.
///     </para>
/// </summary>
public sealed class DevErrorSourceLinkContractTests
{
    private static readonly string _repoRoot = LocateRepoRoot();

    private static string SharedJs =>
        File.ReadAllText(Path.Combine(_repoRoot, "src", "Rask.Core", "Resources", "rask-deverror.ts"));

    [Fact]
    public void The_panel_renders_its_detail_through_the_link_renderer()
    {
        var js = SharedJs;
        var show = js[js.IndexOf("export function showDevError", StringComparison.Ordinal)..];

        Assert.Contains("renderDevErrorDetail(detail", show, StringComparison.Ordinal);
        Assert.DoesNotContain("detail.textContent = info.detail", js, StringComparison.Ordinal);
    }

    [Fact]
    public void Nothing_from_the_app_is_ever_parsed_as_html()
    {
        // The message and the stack are the app's own strings, and an exception message can carry anything
        // a user typed. Text nodes and textContent only.
        Assert.DoesNotContain("innerHTML", SharedJs, StringComparison.Ordinal);
        Assert.DoesNotContain("insertAdjacentHTML", SharedJs, StringComparison.Ordinal);
    }

    [Fact]
    public void Links_open_vscode_at_the_line()
    {
        Assert.Contains("\"vscode://file\"", SharedJs, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("   at Shop.Cart.Add() in /Users/me/Shop/Features/Cart.cs:line 42", "/Users/me/Shop/Features/Cart.cs", "42")]
    [InlineData(@"   at Shop.Cart.Add() in C:\src\Shop\Features\Cart.cs:line 7", @"C:\src\Shop\Features\Cart.cs", "7")]
    public void A_dotnet_frame_with_an_absolute_path_is_linked(string line, string path, string lineNumber)
    {
        var match = Pattern("DEVERR_FRAME").Match(line);

        Assert.True(match.Success);
        Assert.Equal(path, match.Groups[2].Value);
        Assert.Equal(lineNumber, match.Groups[3].Value);
    }

    [Theory]
    [InlineData("/Users/me/Shop/Features/Cart.cs(31,13): error CS0103: The name 'titel' does not exist", "/Users/me/Shop/Features/Cart.cs", "31", "13")]
    public void An_msbuild_error_with_an_absolute_path_is_linked(string line, string path, string lineNumber, string column)
    {
        var match = Pattern("DEVERR_BUILD").Match(line);

        Assert.True(match.Success);
        Assert.Equal(path, match.Groups[1].Value);
        Assert.Equal(lineNumber, match.Groups[2].Value);
        Assert.Equal(column, match.Groups[3].Value);
    }

    [Theory]
    [InlineData("   at Shop.Cart.Add() in Features/Cart.cs:line 42")]          // relative: no root to open it against
    [InlineData("Features/Products/ProductPage.cs(31,13): error CS0103: x")]  // relative MSBuild path
    [InlineData("   at Rask.Core.Component.Render()")]                         // no file at all
    public void A_line_without_an_absolute_path_stays_text(string line)
    {
        Assert.DoesNotMatch(Pattern("DEVERR_FRAME"), line);
        Assert.DoesNotMatch(Pattern("DEVERR_BUILD"), line);
    }

    [Fact]
    public void Deterministic_build_paths_are_not_linked()
    {
        // A Release package's pdb records /_/src/…, which names no file on the developer's machine.
        Assert.Contains("function isDeterministicPath", SharedJs, StringComparison.Ordinal);
        Assert.Contains("indexOf(\"/_/\") === 0", SharedJs, StringComparison.Ordinal);
    }

    /// <summary>The JavaScript regex literal assigned to <paramref name="name" />, compiled as .NET.</summary>
    /// <remarks>Read from the source, so the patterns tested are the ones that ship rather than a copy.</remarks>
    private static Regex Pattern(string name)
    {
        var match = Regex.Match(SharedJs, $@"var {name} = /(.+)/;");
        Assert.True(match.Success, $"rask-deverror.ts no longer declares {name}");

        return new Regex(match.Groups[1].Value);
    }

    private static string LocateRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);

        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Rask.slnx")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("Could not locate the repository root (Rask.slnx).");
    }
}
