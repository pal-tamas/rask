using System.Text.RegularExpressions;
using Rask.Cli.Commands;
using Rask.Cli.Scaffolding;

namespace Rask.Cli.Tests;

/// <summary>
/// Types tutorial chapter 2 into a freshly scaffolded project and builds it — the question the snippet
/// parser can't answer.
/// </summary>
/// <remarks>
/// <para>
/// The chapter is the framework's teaching code now, and until this test existed nothing compiled it.
/// Writing it found four defects that had shipped, none of which a parser could see: the
/// <c>AppDbContext</c> instruction gave the <c>DbSet</c> line but not the <c>using</c> the slice needs;
/// the list page linked to <c>UpdateProduct</c> and <c>DeleteProduct</c>, which the chapter never
/// provided; and the form used <c>DataAnnotationsValidator</c> without saying it comes from its own
/// package. A reader following the chapter exactly got four compiler errors.
/// </para>
/// <para>
/// Opt-in with the rest of the build gates (<c>RASK_CLI_BUILD_E2E=1</c>) because it packs the framework
/// and runs a real restore + build. It reads the chapter rather than a copy of it: a snippet edited in
/// the docs is compiled here, which is the only arrangement that can't drift.
/// </para>
/// </remarks>
public sealed partial class TutorialChapterBuildE2ETests
{
    [SkippableFact]
    public async Task Chapter_2_builds_when_you_type_it_in()
    {
        Skip.IfNot(CliBuildE2E.Enabled, CliBuildE2E.SkipReason);

        var chapter = File.ReadAllText(Path.Combine(TutorialDirectory(), "02-first-feature.md"));
        var fences = CSharpFence().Matches(chapter).Select(m => m.Groups["code"].Value).ToArray();

        string Fence(string contains) =>
            fences.FirstOrDefault(f => f.Contains(contains, StringComparison.Ordinal))
            ?? throw new InvalidOperationException(
                $"Chapter 2 no longer contains a C# snippet with '{contains}'. If the chapter was "
                + "restructured, update this test to match — don't delete the coverage.");

        var (feed, version) = await CliBuildE2E.LocalFeed.Value;
        var temp = Path.Combine(Path.GetTempPath(), "rask-tutorial-e2e", Guid.NewGuid().ToString("N"));
        var projectDir = Path.Combine(temp, "Shop");

        try
        {
            var fs = new SystemFileSystem();

            // Chapter 1: rask new Shop --all-batteries.
            var scaffold = ProjectGenerator.GenerateServer(
                projectDir, "Shop", NewCommand.ToBatteries(["all-batteries"]), version);
            foreach (var file in scaffold.Files)
            {
                fs.CreateDirectory(Path.GetDirectoryName(file.Path)!);
                fs.WriteAllText(file.Path, file.Content);
            }

            // Chapter 2: every file it hands the reader.
            var slice = Path.Combine(projectDir, "Features", "Products");
            fs.CreateDirectory(slice);
            Write(fs, slice, "Product.cs", Fence("class Product : Entity<Guid>"));
            Write(fs, slice, "ProductRequest.cs", Fence("class ProductRequest"));
            Write(fs, slice, "ProductConfiguration.cs", Fence("ProductConfiguration"));
            Write(fs, slice, "UpdateProduct.cs", Fence("UpdateProductCommandHandler"));
            Write(fs, slice, "DeleteProduct.cs", Fence("DeleteProductCommandHandler"));
            Write(fs, slice, "ProductsPage.cs", Fence("class ProductsPage"));

            // The chapter splits CreateProduct across two fences — "and the page that uses it, in the
            // same file" — so they are joined the way a reader would join them.
            Write(fs, slice, "CreateProduct.cs",
                Fence("CreateProductCommandHandler").TrimEnd() + "\n\n" + Fence("[Route(\"/products/new\")]"));

            // …and the two lines it says to add to the context.
            var contextPath = Path.Combine(projectDir, "Features", "Shared", "AppDbContext.cs");
            var context = Strip(Fence("using Shop.Features.Products;")) + "\n" + fs.ReadAllText(contextPath);
            fs.WriteAllText(
                contextPath,
                OpeningBrace().Replace(context, "$1    " + Strip(Fence("DbSet<Product>")) + "\n\n", 1));

            CliBuildE2E.WriteNuGetConfig(fs, projectDir, feed);

            // The one package the chapter tells the reader to add by hand.
            var csproj = Path.Combine(projectDir, "Shop.csproj");
            var (added, addOutput) = await CliBuildE2E.RunDotnet(
                $"add \"{csproj}\" package Rask.Validation.DataAnnotations --version {version}");
            Assert.True(added == 0, $"Adding the validation package failed.{CliBuildE2E.Diagnostics(addOutput)}");

            var (exit, output) = await CliBuildE2E.RunDotnet($"build \"{csproj}\" -warnaserror -m:1");
            Assert.True(
                exit == 0,
                "Tutorial chapter 2 does not compile when typed in as written. Every snippet a reader "
                + $"copies has to build.{CliBuildE2E.Diagnostics(output)}");
        }
        finally
        {
            CliBuildE2E.TryDeleteDirectory(temp);
        }
    }

    /// <summary>A snippet line with its trailing "// at the top of the file" aside removed.</summary>
    private static string Strip(string fence) => fence.Trim().Split("//")[0].Trim();

    private static void Write(IFileSystem fs, string directory, string name, string code) =>
        fs.WriteAllText(Path.Combine(directory, name), code);

    private static string TutorialDirectory()
    {
        for (var dir = AppContext.BaseDirectory; dir is not null; dir = Path.GetDirectoryName(dir))
        {
            if (File.Exists(Path.Combine(dir, "Rask.slnx")))
            {
                return Path.Combine(dir, "docs", "tutorial");
            }
        }

        throw new InvalidOperationException("Could not locate the repo root (Rask.slnx).");
    }

    [GeneratedRegex(@"```csharp\r?\n(?<code>.*?)```", RegexOptions.Singleline)]
    private static partial Regex CSharpFence();

    [GeneratedRegex(@"(\{\n)")]
    private static partial Regex OpeningBrace();
}
