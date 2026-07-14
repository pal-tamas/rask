using Rask.Cli.Commands;

namespace Rask.Cli.Tests;

public sealed class GenerateCommandTests
{
    private const string ProjectDir = "/proj";

    [Fact]
    public async Task Generates_a_page_into_the_project()
    {
        var (console, fs, command) = Build();

        var exit = await command.ExecuteAsync(["page", "Products"], CancellationToken.None);

        Assert.Equal(0, exit);
        var path = Path.GetFullPath("/proj/Features/Products/ProductsPage.cs");
        Assert.True(fs.Files.ContainsKey(path));
        Assert.Contains("namespace MyApp.Features.Products;", fs.Files[path], StringComparison.Ordinal);
        Assert.Contains("Created", console.OutText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Generates_a_component_into_the_project()
    {
        var (_, fs, command) = Build();

        var exit = await command.ExecuteAsync(["component", "PriceTag"], CancellationToken.None);

        Assert.Equal(0, exit);
        Assert.True(fs.Files.ContainsKey(Path.GetFullPath("/proj/Components/PriceTag.cs")));
    }

    [Fact]
    public async Task Dry_run_writes_nothing_but_prints_the_content()
    {
        var (console, fs, command) = Build();

        var exit = await command.ExecuteAsync(["page", "Products", "--dry-run"], CancellationToken.None);

        Assert.Equal(0, exit);
        Assert.DoesNotContain(fs.Files, f => f.Key.EndsWith("ProductsPage.cs", StringComparison.Ordinal));
        Assert.Contains("[dry-run]", console.OutText, StringComparison.Ordinal);
        Assert.Contains("public sealed class ProductsPage", console.OutText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Refuses_to_overwrite_without_force()
    {
        var (console, fs, command) = Build();
        fs.Seed("/proj/Components/PriceTag.cs", "// existing");

        var exit = await command.ExecuteAsync(["component", "PriceTag"], CancellationToken.None);

        Assert.Equal(1, exit);
        Assert.Equal("// existing", fs.Files[Path.GetFullPath("/proj/Components/PriceTag.cs")]);
        Assert.Contains("already exists", console.ErrorText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Force_overwrites_an_existing_file()
    {
        var (_, fs, command) = Build();
        fs.Seed("/proj/Components/PriceTag.cs", "// existing");

        var exit = await command.ExecuteAsync(["component", "PriceTag", "--force"], CancellationToken.None);

        Assert.Equal(0, exit);
        Assert.Contains("class PriceTag", fs.Files[Path.GetFullPath("/proj/Components/PriceTag.cs")], StringComparison.Ordinal);
    }

    [Fact]
    public async Task Invalid_type_name_fails()
    {
        var (console, fs, command) = Build();

        var exit = await command.ExecuteAsync(["component", "2Cool"], CancellationToken.None);

        Assert.Equal(1, exit);
        Assert.DoesNotContain(fs.Files, f => f.Value.Contains("class", StringComparison.Ordinal));
        Assert.Contains("not a valid C# type name", console.ErrorText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Unknown_artifact_fails()
    {
        var (console, _, command) = Build();

        var exit = await command.ExecuteAsync(["controller", "Products"], CancellationToken.None);

        Assert.Equal(1, exit);
        Assert.Contains("Unknown artifact 'controller'", console.ErrorText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Missing_name_fails()
    {
        var (console, _, command) = Build();

        var exit = await command.ExecuteAsync(["page"], CancellationToken.None);

        Assert.Equal(1, exit);
        Assert.Contains("A name is required", console.ErrorText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Invalid_route_is_rejected_before_writing()
    {
        var (console, fs, command) = Build();

        var exit = await command.ExecuteAsync(["page", "Reports", "--route", "a\"b"], CancellationToken.None);

        Assert.Equal(1, exit);
        Assert.DoesNotContain(fs.Files, f => f.Key.EndsWith("ReportsPage.cs", StringComparison.Ordinal));
        Assert.Contains("not a valid route path", console.ErrorText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Reserved_keyword_name_is_rejected()
    {
        var (console, fs, command) = Build();

        var exit = await command.ExecuteAsync(["component", "class"], CancellationToken.None);

        Assert.Equal(1, exit);
        Assert.DoesNotContain(fs.Files, f => f.Value.Contains("class class", StringComparison.Ordinal));
        Assert.Contains("not a valid C# type name", console.ErrorText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Route_on_component_is_rejected()
    {
        var (console, _, command) = Build();

        var exit = await command.ExecuteAsync(["component", "PriceTag", "--route", "/x"], CancellationToken.None);

        Assert.Equal(1, exit);
        Assert.Contains("--route only applies", console.ErrorText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Fails_cleanly_outside_a_project()
    {
        var console = new StringConsole();
        var fs = new FakeFileSystem(); // no csproj seeded
        var command = new GenerateCommand(console, fs, ProjectDir);

        var exit = await command.ExecuteAsync(["component", "PriceTag"], CancellationToken.None);

        Assert.Equal(1, exit);
        Assert.Contains("Couldn't find a single .csproj", console.ErrorText, StringComparison.Ordinal);
    }

    private static (StringConsole Console, FakeFileSystem Fs, GenerateCommand Command) Build()
    {
        var console = new StringConsole();
        var fs = new FakeFileSystem();
        fs.Seed("/proj/MyApp.csproj", "<Project></Project>");
        return (console, fs, new GenerateCommand(console, fs, ProjectDir));
    }
}
