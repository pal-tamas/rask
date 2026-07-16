using Rask.Cli.Templates;

namespace Rask.Cli.Tests;

public sealed class TemplateCatalogTests
{
    [Theory]
    [InlineData("server")]
    [InlineData("wasm")]
    [InlineData("wasm-hosted")]
    [InlineData("native")]
    public void Resolves_each_known_template_by_key(string key)
    {
        Assert.True(TemplateCatalog.TryGet(key, out var template));
        Assert.Equal(key, template.Key);
    }

    [Fact]
    public void Lookup_is_case_insensitive()
    {
        Assert.True(TemplateCatalog.TryGet("SERVER", out var template));
        Assert.Equal("server", template.Key);
    }

    [Fact]
    public void Unknown_key_falls_back_to_default_and_returns_false()
    {
        Assert.False(TemplateCatalog.TryGet("angular", out var template));
        Assert.Equal(TemplateCatalog.Default, template);
    }

    [Fact]
    public void Default_is_the_server_template()
    {
        Assert.Equal("server", TemplateCatalog.Default.Key);
    }

    [Fact]
    public void Server_supports_cqrs_but_wasm_does_not()
    {
        TemplateCatalog.TryGet("server", out var server);
        TemplateCatalog.TryGet("wasm", out var wasm);

        Assert.Contains("cqrs", server.SupportedFlags);
        Assert.DoesNotContain("cqrs", wasm.SupportedFlags);
    }

    [Fact]
    public void Native_supports_no_web_feature_flags()
    {
        TemplateCatalog.TryGet("native", out var native);

        Assert.Empty(native.SupportedFlags);
    }
}
