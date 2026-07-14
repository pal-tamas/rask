using Rask.Cli.Scaffolding;

namespace Rask.Cli.Tests;

public sealed class IdentifiersTests
{
    [Theory]
    [InlineData("Products", true)]
    [InlineData("_Widget", true)]
    [InlineData("Price2", true)]
    [InlineData("2Price", false)]
    [InlineData("My-Page", false)]
    [InlineData("has space", false)]
    [InlineData("", false)]
    [InlineData("class", false)]     // reserved keyword
    [InlineData("int", false)]       // reserved keyword
    [InlineData("namespace", false)] // reserved keyword
    [InlineData("var", true)]        // contextual keyword — a legal identifier
    public void IsValidTypeName(string value, bool expected) =>
        Assert.Equal(expected, Identifiers.IsValidTypeName(value));

    [Theory]
    [InlineData("/products", true)]
    [InlineData("/orders/{id:int}", true)]
    [InlineData("a\"b", false)]
    [InlineData("a\\b", false)]
    public void IsValidRoutePath(string route, bool expected) =>
        Assert.Equal(expected, Identifiers.IsValidRoutePath(route));

    [Theory]
    [InlineData("Products", "/products")]
    [InlineData("ProductList", "/product-list")]
    [InlineData("Orders", "/orders")]
    public void ToRoutePath_kebab_cases(string name, string expected) =>
        Assert.Equal(expected, Identifiers.ToRoutePath(name));

    [Theory]
    [InlineData("Features", "Features")]
    [InlineData("my-feature", "myfeature")]
    [InlineData("2nd", "_2nd")]
    public void ToNamespacePart_sanitizes(string segment, string expected) =>
        Assert.Equal(expected, Identifiers.ToNamespacePart(segment));
}

public sealed class ProjectContextTests
{
    [Fact]
    public void NamespaceFor_root_directory_is_root_namespace()
    {
        var project = new ProjectContext("/proj", "MyApp");

        Assert.Equal("MyApp", project.NamespaceFor("/proj"));
    }

    [Fact]
    public void NamespaceFor_subfolders_extend_the_namespace()
    {
        var project = new ProjectContext("/proj", "MyApp");

        Assert.Equal("MyApp.Features.Products", project.NamespaceFor("/proj/Features/Products"));
    }

    [Fact]
    public void ReadRootNamespace_prefers_explicit_element()
    {
        var fs = new FakeFileSystem();
        fs.Seed("/proj/MyApp.csproj", "<Project><PropertyGroup><RootNamespace>Acme.Store</RootNamespace></PropertyGroup></Project>");

        Assert.Equal("Acme.Store", ProjectContext.ReadRootNamespace(fs, "/proj/MyApp.csproj"));
    }

    [Fact]
    public void ReadRootNamespace_falls_back_to_project_file_name()
    {
        var fs = new FakeFileSystem();
        fs.Seed("/proj/Acme.Store.csproj", "<Project></Project>");

        Assert.Equal("Acme.Store", ProjectContext.ReadRootNamespace(fs, "/proj/Acme.Store.csproj"));
    }

    [Fact]
    public void ReadRootNamespace_sanitizes_an_invalid_explicit_value()
    {
        var fs = new FakeFileSystem();
        fs.Seed("/proj/App.csproj", "<Project><PropertyGroup><RootNamespace>1Store</RootNamespace></PropertyGroup></Project>");

        // Must not return the raw "1Store" (a namespace can't start with a digit).
        Assert.Equal("_1Store", ProjectContext.ReadRootNamespace(fs, "/proj/App.csproj"));
    }
}

public sealed class ProjectLocatorTests
{
    [Fact]
    public void Locate_walks_up_to_the_nearest_csproj()
    {
        var fs = new FakeFileSystem();
        fs.Seed("/proj/MyApp.csproj", "<Project></Project>");

        var project = ProjectLocator.Locate(fs, "/proj/Features/Products");

        Assert.NotNull(project);
        Assert.Equal(Path.GetFullPath("/proj"), project!.ProjectDirectory);
        Assert.Equal("MyApp", project.RootNamespace);
    }

    [Fact]
    public void Locate_returns_null_when_no_project_is_found()
    {
        var fs = new FakeFileSystem();

        Assert.Null(ProjectLocator.Locate(fs, "/nowhere/here"));
    }
}
