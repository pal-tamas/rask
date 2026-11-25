using System.Text.RegularExpressions;
using Rask.Cli.Commands;
using Rask.Cli.Scaffolding;
using Rask.Cli.Templates;

namespace Rask.Cli.E2E.Tests;

/// <summary>
/// Walks the tutorial the way a reader does — scaffold, then chapter 2, 3, 4 in order — building the app AND its
/// tests after each, then running the tests. The question the snippet parser can't answer.
/// </summary>
/// <remarks>
/// <para>
/// The chapters are the framework's teaching code, and every one of them is compiled here: the aggregates, their value
/// objects and typed ids, the pages whose forms are generated from the aggregates' operations (<c>Form.For</c>), the
/// jobs, handlers and events — and, since the tutorial teaches that a rule is proved where it lives, the tests each
/// chapter writes into <c>Shop.Tests</c>. A <c>Form.For(Order.Ship, id)</c> only exists once the operation generator
/// has run over the chapter's <c>Order</c>, and <c>Test.FormShows&lt;ProductAddInput&gt;</c> only once it has written
/// that input, so this walk is what proves the generated surface the chapters type against.
/// </para>
/// <para>
/// The chapters build on each other — chapter 4's job calls an operation chapter 3's <c>Order</c> gains, chapter 7's
/// handler on <c>Product</c> hears an event chapter 7's <c>Order</c> raises — so they are walked cumulatively rather
/// than in isolation. Most snippets are whole files. The few a reader types INTO a file (two members at the end of a
/// class, a line after a line) are applied the way the chapter says, so the edit the prose describes is the edit that
/// is compiled.
/// </para>
/// <para>
/// Rewriting the tutorial to aggregates found, on the way in: a scaffold whose <c>App</c> mounts no <c>Ui.Confirm</c>,
/// so an editor dialog's Delete had nowhere to ask (chapter 2 adds it); and that a test database has no cache or job
/// battery, so a save whose in-memory handler reaches <c>Cache</c> fails the test (chapter 6 fakes them).
/// </para>
/// <para>
/// Opt-in with the rest of the build gates (<c>RASK_CLI_BUILD_E2E=1</c>) because it packs the framework and runs a
/// real restore, build and test. It reads the chapters rather than copies of them: a snippet edited in the docs is
/// compiled here, which is the only arrangement that can't drift.
/// </para>
/// </remarks>
public sealed partial class TutorialChapterBuildE2ETests
{
    [Fact]
    public async Task The_tutorial_builds_and_its_tests_pass_when_you_type_it_in()
    {
        Assert.SkipUnless(CliBuildE2E.Enabled, CliBuildE2E.SkipReason);

        var ch2 = Fences("02-first-feature.md");
        var ch3 = Fences("03-orders-and-auth.md");
        var ch4 = Fences("04-background-jobs.md");
        var ch5 = Fences("05-email.md");
        var ch6 = Fences("06-cache.md");
        var ch7 = Fences("07-outbox-events.md");
        var ch9 = Fences("09-web-push.md");
        var ch10 = Fences("10-ops.md");

        var (feed, version) = await CliBuildE2E.LocalFeed.Value;
        var temp = Path.Combine(Path.GetTempPath(), "rask-tutorial-e2e", Guid.NewGuid().ToString("N"));
        var projectDir = Path.Combine(temp, "Shop");

        try
        {
            var fs = new SystemFileSystem();

            // Chapter 1: rask new Shop — every battery, and the Shop.Tests project beside the app.
            var scaffold = ProjectGenerator.GenerateServer(
                projectDir, "Shop",
                BatterySelection.ToBatteries(TemplateCatalog.Default, []), version);
            foreach (var file in scaffold.Files)
            {
                fs.CreateDirectory(Path.GetDirectoryName(file.Path)!);
                fs.WriteAllText(file.Path, file.Content);
            }

            CliBuildE2E.WriteNuGetConfig(fs, projectDir, feed);

            var tests = Path.Combine(projectDir, "Shop.Tests");
            var testsCsproj = Path.Combine(tests, "Shop.Tests.csproj");
            Assert.True(File.Exists(testsCsproj), "rask new no longer writes Shop.Tests, which every chapter writes its tests into.");

            var shared = Path.Combine(projectDir, "Features", "Shared");
            var products = Path.Combine(projectDir, "Features", "Products");
            var orders = Path.Combine(projectDir, "Features", "Orders");
            var productTests = Path.Combine(tests, "Features", "Products");
            var orderTests = Path.Combine(tests, "Features", "Orders");

            // --- Chapter 2: the Product aggregate, the one page over it, the App that mounts the editor's confirm, the tests ---
            Write(fs, shared, "App.cs", Pick(ch2, "Ui.Toast, Ui.Confirm", "2"));
            Write(fs, products, "Product.cs", Pick(ch2, "class Product : Aggregate<ProductId", "2"));
            Write(fs, products, "ProductsPage.cs", Pick(ch2, "class ProductsPage", "2"));
            Write(fs, productTests, "ProductTests.cs", Pick(ch2, "class ProductTests", "2"));

            await Build(testsCsproj, "chapter 2");

            // --- Chapter 3: Order with its lines, its page, the roles on Product's operations, the tests that sign in ---
            Write(fs, products, "Product.cs", Pick(ch3, "b.Operation(nameof(Add)", "3"));
            Replace(
                fs, Path.Combine(products, "ProductsPage.cs"),
                """Ui.Button.Primary.Href(route.With("edit", UiEditorDialog.New))["New product"]""",
                Pick(ch3, "Product.MayAdd ?", "3").Trim());
            Replace(
                fs, Path.Combine(products, "ProductsPage.cs"),
                """Ui.RowActions.Edit(route.With("edit", p.Id.ToString()))""",
                Pick(ch3, "Product.MayChange ?", "3").Trim());
            Write(fs, orders, "Order.cs", Pick(ch3, "class Order : Aggregate<OrderId", "3"));
            Write(fs, orders, "OrderLine.cs", Pick(ch3, "class OrderLine : Entity<OrderLineId>", "3"));
            Write(fs, orders, "OrdersPage.cs", Pick(ch3, "class OrdersPage", "3"));
            Write(fs, tests, "Signed.cs", Pick(ch3, "class Signed", "3"));
            Write(fs, productTests, "ProductTests.cs", Pick(ch3, "class ProductTests", "3"));
            Write(fs, orderTests, "OrderTests.cs", Pick(ch3, "class OrderTests", "3"));

            await Build(testsCsproj, "chapter 3");

            // --- Chapter 4: two members at the end of Order, the job that calls one, the button that queues it ---
            AppendMembers(fs, Path.Combine(orders, "Order.cs"), Pick(ch4, "public void MarkReceiptSent()", "4"));
            Write(fs, orders, "SendOrderReceipt.cs", Pick(ch4, "record SendOrderReceipt(OrderId Order)", "4"));
            Write(fs, orders, "SendReceiptButton.cs", Pick(ch4, "class SendReceiptButton", "4"));

            // "Put it in the order's editor, under the ship form": the fence is the ship form and the new line.
            var underShip = Pick(ch4, "SendReceiptButton.Order(id),", "4");
            Replace(
                fs, Path.Combine(orders, "OrdersPage.cs"),
                underShip[..underShip.IndexOf("        SendReceiptButton.Order(id),", StringComparison.Ordinal)],
                underShip);
            Write(fs, orderTests, "ReceiptTests.cs", Pick(ch4, "class ReceiptTests", "4"));

            await Build(testsCsproj, "chapter 4");

            // --- Chapter 5: the email body, a plain component, and the job that now sends it ---
            Write(fs, orders, "OrderReceipt.cs", Pick(ch5, "Thanks for your order!", "5"));
            Write(fs, orders, "SendOrderReceipt.cs", Pick(ch5, "Mail.Send(", "5"));
            Write(fs, orderTests, "ReceiptTests.cs", Pick(ch5, "class ReceiptTests", "5"));

            await Build(testsCsproj, "chapter 5");

            // --- Chapter 6: the catalog's event, the cache and its handler, the page over it, the batteries faked ---
            Write(fs, products, "Product.cs", Pick(ch6, "class Product : Aggregate<ProductId", "6"));
            Write(fs, products, "CatalogCache.cs", Pick(ch6, "class CatalogCache", "6"));
            Write(fs, products, "ProductsPage.cs", Pick(ch6, "class ProductsPage", "6"));
            Write(fs, tests, "FakeBatteries.cs", Pick(ch6, "class FakeBatteries", "6"));

            // "Add this line after each TestDatabase.Start in ProductTests, OrderTests and ReceiptTests."
            var fakeLine = Pick(ch6, "using var batteries = new FakeBatteries();", "6").Trim();
            foreach (var file in new[]
                     {
                         Path.Combine(productTests, "ProductTests.cs"), Path.Combine(orderTests, "OrderTests.cs"),
                         Path.Combine(orderTests, "ReceiptTests.cs"),
                     })
            {
                AddAfterEach(fs, file, "await using var database = await TestDatabase.Start(", fakeLine);
            }

            Write(fs, productTests, "CatalogCacheTests.cs", Pick(ch6, "class CatalogCacheTests", "6"));

            await Build(testsCsproj, "chapter 6");

            // --- Chapter 7: OrderPlaced, raised by Order.Place; Product's handler on it; the durable receipt handler ---
            Write(fs, orders, "OrderEvents.cs", Pick(ch7, "record OrderPlaced", "7"));
            Write(fs, orders, "Order.cs", Pick(ch7, "class Order : Aggregate<OrderId", "7"));
            Write(fs, products, "Product.Sales.cs", Pick(ch7, "Task On(OrderPlaced e)", "7"));
            Write(fs, orders, "OrderPlacedHandler.cs", Pick(ch7, "IDurableHandler<OrderPlaced>", "7"));
            Write(fs, tests, "FakeBatteries.cs", Pick(ch7, "class FakeBatteries", "7"));
            Write(fs, orderTests, "OrderPlacedTests.cs", Pick(ch7, "class OrderPlacedTests", "7"));

            await Build(testsCsproj, "chapter 7");

            // --- Chapter 9: the subscribe button, the order's account and OrderShipped, the push handler ---
            Write(fs, orders, "NotifyMe.cs", Pick(ch9, "class NotifyMe", "9"));
            Write(fs, orders, "Order.cs", Pick(ch9, "class Order : Aggregate<OrderId", "9"));
            Write(fs, orders, "OrderEvents.cs", Pick(ch9, "record OrderShipped", "9"));
            Write(fs, orders, "OrderShippedHandler.cs", Pick(ch9, "IDurableHandler<OrderShipped>", "9"));
            Write(fs, orderTests, "OrderShippedTests.cs", Pick(ch9, "class OrderShippedTests", "9"));

            await Build(testsCsproj, "chapter 9");

            // --- Chapter 10: the ops page, over every battery's own table ---
            var ops = Path.Combine(projectDir, "Features", "Ops");
            Write(fs, ops, "OpsPage.cs", Pick(ch10, "[Route(\"/ops\")]", "10"));

            await Build(testsCsproj, "chapter 10");

            // And what every chapter proved, on SQLite in memory: the tests the reader typed in pass.
            var (exit, output) = await CliBuildE2E.RunDotnet($"test \"{testsCsproj}\" --no-build");
            Assert.True(
                exit == 0,
                "The tutorial's tests, typed in as written, do not pass. Every rule a chapter proves has to hold.\n"
                + string.Join('\n', output.Split('\n').Where(Failing).Take(40)));
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

    // A line of `dotnet test` output that says what failed and why.
    private static bool Failing(string line) =>
        line.Contains("Failed ", StringComparison.Ordinal)
        || line.Contains("Error Message", StringComparison.Ordinal)
        || line.Contains("Exception", StringComparison.Ordinal);

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

    private static void Write(IFileSystem fs, string directory, string name, string code)
    {
        fs.CreateDirectory(directory);
        fs.WriteAllText(Path.Combine(directory, name), code);
    }

    /// <summary>A chapter's "change this to that" — the old text must be there, or the chapter no longer matches the file.</summary>
    private static void Replace(IFileSystem fs, string path, string old, string replacement)
    {
        var text = fs.ReadAllText(path);
        Assert.True(
            text.Contains(old, StringComparison.Ordinal),
            $"{Path.GetFileName(path)} no longer holds the text a chapter tells the reader to change:\n{old}");
        fs.WriteAllText(path, text.Replace(old, replacement, StringComparison.Ordinal));
    }

    /// <summary>A chapter's "add these members at the end of the class": before the file's last closing brace.</summary>
    private static void AppendMembers(IFileSystem fs, string path, string members)
    {
        var text = fs.ReadAllText(path).TrimEnd();
        Assert.EndsWith("}", text, StringComparison.Ordinal);
        fs.WriteAllText(path, text[..^1].TrimEnd() + "\n\n" + members.TrimEnd() + "\n}\n");
    }

    /// <summary>A chapter's "add this line after each …": the line, indented as the one it follows.</summary>
    private static void AddAfterEach(IFileSystem fs, string path, string anchor, string line)
    {
        var lines = new List<string>();
        foreach (var existing in fs.ReadAllText(path).Split('\n'))
        {
            lines.Add(existing);
            if (existing.Contains(anchor, StringComparison.Ordinal))
            {
                lines.Add(existing[..(existing.Length - existing.TrimStart().Length)] + line);
            }
        }

        Assert.True(lines.Count > fs.ReadAllText(path).Split('\n').Length, $"{Path.GetFileName(path)} has no '{anchor}' for the chapter's line to follow.");
        fs.WriteAllText(path, string.Join('\n', lines));
    }

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
