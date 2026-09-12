using System.Text.RegularExpressions;
using Rask.Cli.Commands;
using Rask.Cli.Scaffolding;
using Rask.Cli.Templates;

namespace Rask.Cli.E2E.Tests;

/// <summary>
/// Walks the tutorial the way a reader does — scaffold, then chapter 2, 3, 4 in order — building after
/// each. The question the snippet parser can't answer.
/// </summary>
/// <remarks>
/// <para>
/// The chapter is the framework's teaching code now, and until this test existed nothing compiled it.
/// Writing it found four defects that had shipped, none of which a parser could see: a context
/// instruction gave the <c>DbSet</c> line but not the <c>using</c> the slice needs; the list page linked
/// to <c>UpdateProduct</c> and <c>DeleteProduct</c>, which the chapter never provided; and the form used
/// <c>DataAnnotationsValidator</c> without saying it comes from its own package. A reader following the
/// chapter exactly got four compiler errors.
/// </para>
/// <para>
/// Extending it found more of the same. Chapter 4's job handler used a context factory with no
/// <c>using</c> and no namespace. Chapter 7 told the reader to give <c>Order</c> a <c>Customer</c> field
/// it never had — its snippets came from a generator run with different fields than chapter 3 used, which
/// the old <c>--force</c> regeneration papered over and a reader patching by hand cannot.
/// </para>
/// <para>
/// The pages read and write through the model surface — <c>Product.FindAsync</c>,
/// <c>Product.AsQueryable()</c> and the generated <c>Product.CreateAsync(ProductModel)</c> — so nothing
/// here patches a context: declaring the entity is the whole of the data code a reader types, and this
/// walk is what proves the generated form model and writes exist for it.
/// </para>
/// <para>
/// The chapters build on each other — chapter 4's handler reads <c>Order</c>, which only exists after
/// chapter 3; chapter 7 rewrites the <c>Order</c> chapter 3 wrote and its handler enqueues chapter 4's
/// job — so they are walked cumulatively rather than in isolation. Chapter 6 is absent because its
/// accessor snippet is elided (<c>…</c>), leaving no complete file to write.
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

            // Chapter 2: every file it hands the reader. The entity is the only data code in it — the form
            // model and the writes the pages call are generated from it, and it is mapped with no context
            // to edit, so there is nothing else to overlay.
            var slice = Path.Combine(projectDir, "Features", "Products");
            fs.CreateDirectory(slice);
            Write(fs, slice, "Product.cs", Fence("class Product : Model<Guid>"));
            Write(fs, slice, "CreateProduct.cs", Fence("[Route(\"/products/new\")]"));
            Write(fs, slice, "UpdateProduct.cs", Fence("[Route(\"/products/{id:guid}/edit\")]"));
            Write(fs, slice, "DeleteProduct.cs", Fence("class DeleteProduct : Component"));
            Write(fs, slice, "ProductsPage.cs", Fence("class ProductsPage"));

            CliBuildE2E.WriteNuGetConfig(fs, projectDir, feed);

            // No package is added here, and that is the assertion. Chapter 2 puts [Required] on an entity
            // and expects the generated form model to enforce it; validation ships inside Rask.Core, so a
            // project straight out of `rask new` already has it. If it ever stops being built in, this build
            // still succeeds and the chapter still compiles — so the guarantee is pinned by the unit suite
            // (Rask.Validation.Tests), and this gate only has to prove no `dotnet add package` is needed.
            var csproj = Path.Combine(projectDir, "Shop.csproj");

            await Build(csproj, "chapter 2");

            // --- Chapter 3: a second entity on the same database ---
            var orders = Path.Combine(projectDir, "Features", "Orders");
            fs.CreateDirectory(orders);
            Write(fs, orders, "Order.cs", Pick(ch3, "class Order : Model<Guid>", "3"));

            await Build(csproj, "chapter 3");

            // --- Chapter 4: a durable job, whose handler reads the Order chapter 3 added ---
            // The chapter shows the record and the filled-in handler as separate snippets, the handler's
            // `using` above it; they are joined into one file the way a reader would join them.
            var jobHandler = Pick(ch4, "Order.FindAsync(job.OrderId", "4");
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

            // --- Chapter 7: a domain event through the outbox ---
            // The chapter shows the revised Order.cs whole rather than as a patch, which is both what a
            // reader needs (the Raise call has to go somewhere specific) and what lets this walk apply
            // it — a fragment could not replace the file chapter 3 wrote.
            //
            // PlaceOrder is the chapter's one piece of plain EF Core — IDbContextFactory<AppDbContext>,
            // Order.Place, db.Set<Order>(), SaveChangesAsync — and it is a whole component precisely so it
            // is compiled here: it is the only snippet that names the scaffold's context type, so it is the
            // one that breaks if the context the scaffold writes and the context the tutorial teaches drift.
            Write(fs, orders, "OrderEvents.cs", Pick(ch7, "record OrderPlaced", "7"));
            Write(fs, orders, "Order.cs", Pick(ch7, "Raise(new OrderPlaced", "7"));
            Write(fs, orders, "PlaceOrder.cs", Pick(ch7, "Order.Place(ProductId", "7"));
            Write(fs, orders, "OrderPlacedHandler.cs", Pick(ch7, "INotificationHandler<OrderPlaced>", "7"));

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
}
