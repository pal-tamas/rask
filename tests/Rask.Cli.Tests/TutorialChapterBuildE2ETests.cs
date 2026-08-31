using System.Text.RegularExpressions;
using Rask.Cli.Commands;
using Rask.Cli.Scaffolding;
using Rask.Cli.Templates;

namespace Rask.Cli.Tests;

/// <summary>
/// Walks the tutorial the way a reader does — scaffold, then chapter 2, 3, 4 in order — building after
/// each. The question the snippet parser can't answer.
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
/// Extending it found more of the same. Chapter 4's job handler used <c>IDbContextFactory</c> and
/// <c>AppDbContext</c> with no <c>using</c> and no namespace. Chapter 7 told the reader to give
/// <c>Order</c> a <c>Customer</c> field it never had — its snippets came from a generator run with
/// different fields than chapter 3 used, which the old <c>--force</c> regeneration papered over and a
/// reader patching by hand cannot.
/// </para>
/// <para>
/// The chapters build on each other — chapter 4's handler reads <c>db.Orders</c>, which only exists
/// after chapter 3; chapter 7 rewrites the <c>Order</c> chapter 3 wrote — so they are walked
/// cumulatively rather than in isolation. Chapter 6 is absent because its accessor snippet is elided
/// (<c>…</c>), leaving no complete file to write.
/// </para>
/// <para>
/// Opt-in with the rest of the build gates (<c>RASK_CLI_BUILD_E2E=1</c>) because it packs the framework
/// and runs a real restore + build. It reads the chapters rather than copies of them: a snippet edited
/// in the docs is compiled here, which is the only arrangement that can't drift.
/// </para>
/// </remarks>
public sealed partial class TutorialChapterBuildE2ETests
{
    [SkippableFact]
    public async Task The_tutorial_builds_when_you_type_it_in()
    {
        Skip.IfNot(CliBuildE2E.Enabled, CliBuildE2E.SkipReason);

        var ch2 = Fences("02-first-feature.md");
        var ch3 = Fences("03-orders-and-auth.md");
        var ch4 = Fences("04-background-jobs.md");
        var ch5 = Fences("05-email.md");
        var ch7 = Fences("07-outbox-events.md");

        string Fence(string contains) => Pick(ch2, contains, "2");

        var (feed, version) = await CliBuildE2E.LocalFeed.Value;
        var temp = Path.Combine(Path.GetTempPath(), "rask-tutorial-e2e", Guid.NewGuid().ToString("N"));
        var projectDir = Path.Combine(temp, "Shop");

        try
        {
            var fs = new SystemFileSystem();

            // Chapter 1: rask new Shop --bootstrap. The batteries are the default now, so this is
            // simply what the template supports. Auth is left off deliberately — the chapter's own
            // files are overlaid below, and scaffolding a second copy would collide with them.
            var scaffold = ProjectGenerator.GenerateServer(
                projectDir, "Shop",
                NewCommand.ToBatteries(TemplateCatalog.Default, []), version);
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

            await Build(csproj, "chapter 2");

            // --- Chapter 3: a second slice on the same database ---
            var orders = Path.Combine(projectDir, "Features", "Orders");
            fs.CreateDirectory(orders);
            Write(fs, orders, "Order.cs", Pick(ch3, "class Order : Entity<Guid>", "3"));

            context = Strip(Pick(ch3, "using Shop.Features.Orders;", "3")) + "\n" + fs.ReadAllText(contextPath);
            fs.WriteAllText(
                contextPath,
                OpeningBrace().Replace(context, "$1    " + Strip(Pick(ch3, "DbSet<Order>", "3")) + "\n\n", 1));

            await Build(csproj, "chapter 3");

            // --- Chapter 4: a durable job, whose handler reads the Orders set chapter 3 added ---
            var jobHandler = Pick(ch4, "SendOrderReceiptHandler(IDbContextFactory", "4");
            var shared = Path.Combine(projectDir, "Features", "Shared");
            Write(
                fs, shared, "SendOrderReceipt.cs",
                jobHandler[..jobHandler.IndexOf("public sealed class", StringComparison.Ordinal)].Trim()
                + "\n\nnamespace Shop.Features.Shared;\n\n"
                + Pick(ch4, "record SendOrderReceipt(Guid OrderId)", "4").Trim() + "\n\n"
                + jobHandler[jobHandler.IndexOf("public sealed class", StringComparison.Ordinal)..]);

            await Build(csproj, "chapter 4");

            // --- Chapter 5: the email body, a plain component ---
            Write(fs, shared, "OrderReceipt.cs", Pick(ch5, "Thanks for your order!", "5"));

            await Build(csproj, "chapter 5");

            // --- Chapter 7: domain events through the outbox ---
            // The chapter shows the revised Order.cs whole rather than as a patch, which is both what a
            // reader needs (the Raise calls have to go somewhere specific) and what lets this walk apply
            // it — a fragment could not replace the file chapter 3 wrote.
            Write(fs, orders, "OrderEvents.cs", Pick(ch7, "record OrderCreated", "7"));
            Write(fs, orders, "Order.cs", Pick(ch7, "entity.Raise(new OrderCreated", "7"));
            Write(fs, orders, "OrderCreatedHandler.cs", Pick(ch7, "INotificationHandler<OrderCreated>", "7"));

            await Build(csproj, "chapter 7");
        }
        finally
        {
            CliBuildE2E.TryDeleteDirectory(temp);
        }
    }

    private static async Task Build(string csproj, string chapter)
    {
        var (exit, output) = await CliBuildE2E.RunDotnet($"build \"{csproj}\" -warnaserror -m:1");
        Assert.True(
            exit == 0,
            $"Tutorial {chapter} does not compile when typed in as written. Every snippet a reader "
            + $"copies has to build.{CliBuildE2E.Diagnostics(output)}");
    }

    private static string[] Fences(string chapter) =>
        CSharpFence()
            .Matches(File.ReadAllText(Path.Combine(TutorialDirectory(), chapter)))
            .Select(m => m.Groups["code"].Value)
            .ToArray();

    /// <summary>
    /// The chapter's snippet containing <paramref name="contains"/>. Throws rather than returning null so
    /// a restructured chapter fails loudly instead of silently covering nothing.
    /// </summary>
    private static string Pick(string[] fences, string contains, string chapter) =>
        fences.FirstOrDefault(f => f.Contains(contains, StringComparison.Ordinal))
        ?? throw new InvalidOperationException(
            $"Chapter {chapter} no longer contains a C# snippet with '{contains}'. If the chapter was "
            + "restructured, update this test to match — don't delete the coverage.");

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
