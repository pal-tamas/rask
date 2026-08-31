using Rask.Cli.Templates;

namespace Rask.Cli.Tests;

public sealed class TemplateCatalogTests
{
    [Theory]
    [InlineData("server")]
    [InlineData("wasm")]
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
        // Deliberately not a plausible framework name: "angular" stood in for "unknown" here until
        // Angular became a template, which turned this into a test of nothing.
        Assert.False(TemplateCatalog.TryGet("cobol", out var template));
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
    public void Server_supports_data_but_wasm_does_not()
    {
        TemplateCatalog.TryGet("server", out var server);
        TemplateCatalog.TryGet("wasm", out var wasm);

        Assert.Contains("data", server.SupportedFlags);
        Assert.DoesNotContain("data", wasm.SupportedFlags);
    }

    /// <summary>
    ///     The native template is gone with the native hosting model — Rask is a web framework.
    /// </summary>
    /// <remarks>
    ///     Removing the host packages left this catalog entry behind, and a catalog entry is the whole
    ///     contract: it is what <c>--template</c> validates against, what the wizard lists, and what shell
    ///     completion offers. The generator's switch had no native arm, so it fell through to the default
    ///     one — <c>rask new Field --template native</c> announced "Creating Rask native mobile app (iOS +
    ///     Android)", wrote an ASP.NET host, and signed off with "Created Field (Rask server app)". Nothing
    ///     threw, so nothing caught it. Assert the absence, because the presence is what shipped.
    /// </remarks>
    [Theory]
    [InlineData("native")]
    [InlineData("wasm-hosted")]
    public void A_removed_template_is_gone(string key)
    {
        Assert.False(TemplateCatalog.TryGet(key, out _));
        Assert.DoesNotContain(key, TemplateCatalog.Keys, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void Every_supported_flag_is_a_flag_rask_new_actually_accepts()
    {
        // A template may support fewer flags than `rask new` declares, but never more: a supported flag
        // that isn't in FeatureFlags can never be requested, so it is dead weight that reads as a feature.
        // `litestream` sat here unreachable until this guard was added.
        var declared = new HashSet<string>(Commands.NewCommand.FeatureFlags, StringComparer.Ordinal);

        var unreachable = TemplateCatalog.All
            .SelectMany(template => template.SupportedFlags.Select(flag => $"{template.Key}: {flag}"))
            .Where(entry => !declared.Contains(entry.Split(": ")[1]))
            .ToArray();

        Assert.Empty(unreachable);
    }
}
