using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Rask.Generators.Analyzers;

namespace Rask.Generators.Tests;

/// <summary>
///     RASK122 — <c>IUserProvider.Current</c>, or something read from it, kept in a field or property by a type that
///     never subscribes to <c>IUserProvider.Changed</c>, so it goes on showing whoever was signed in when it was read.
/// </summary>
public class CachedUserAnalyzerTests
{
    // ---------- it fires ----------

    [Fact]
    public async Task A_name_read_from_the_user_in_OnMount_and_kept_in_a_field_is_reported()
    {
        var members = """
            private string? _name;
            protected override async Task OnMount() => _name = users.Current.Identity?.Name;
            """;

        var d = Assert.Single(await AnalyzeAsync(members));

        Assert.Equal("RASK122", d.Id);
        Assert.Contains("'_name'", d.GetMessage(), StringComparison.Ordinal);
        Assert.Contains("'Page'", d.GetMessage(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_field_initialized_from_the_user_is_reported()
    {
        var members = "private readonly bool _isAdmin = users.Current.IsInRole(\"admin\");";

        var diagnostics = await AnalyzeAsync(members);

        Assert.Equal("RASK122", Assert.Single(diagnostics).Id);
    }

    [Fact]
    public async Task A_property_holding_the_whole_principal_is_reported()
    {
        var members = """
            private ClaimsPrincipal? Signed { get; set; }
            protected override async Task OnMount() => Signed = users.Current;
            """;

        var diagnostics = await AnalyzeAsync(members);

        Assert.Equal("RASK122", Assert.Single(diagnostics).Id);
    }

    // ---------- it stays quiet ----------

    [Fact]
    public async Task Reading_the_user_where_it_is_shown_is_not_reported()
    {
        var members = "protected override Component? Render() => P[users.Current.Identity?.Name ?? \"\"];";

        var diagnostics = await AnalyzeAsync(members, render: false);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task A_kept_user_that_follows_Changed_is_not_reported()
    {
        var members = """
            private string? _name;
            protected override async Task OnMount()
            {
                _name = users.Current.Identity?.Name;
                users.Changed += (_, _) => _name = users.Current.Identity?.Name;
            }
            """;

        var diagnostics = await AnalyzeAsync(members);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task A_field_holding_a_lambda_that_reads_the_user_is_not_reported()
    {
        var members = """
            private Func<string?> _name = () => null;
            protected override async Task OnMount() => _name = () => users.Current.Identity?.Name;
            """;

        var diagnostics = await AnalyzeAsync(members);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task A_copy_in_a_local_is_not_reported()
    {
        var members = """
            private int _n;
            protected override async Task OnMount()
            {
                var user = users.Current;
                _n = user.Claims.Count();
            }
            """;

        var diagnostics = await AnalyzeAsync(members);

        // `_n` is computed from a local, so the rule does not follow it: low noise over full reach.
        Assert.Empty(diagnostics);
    }

    private static Task<ImmutableArray<Diagnostic>> AnalyzeAsync(string members, bool render = true) =>
        CompiledAnalysis.RunAsync(
            BuilderGeneratorHarness.Compile($$"""
                using System;
                using System.Linq;
                using System.Security.Claims;
                using System.Threading.Tasks;
                using Rask.Core;
                using Rask.Core.Authentication;
                namespace Demo
                {
                    public sealed partial class Page(IUserProvider users) : Component
                    {
                        {{members}}
                        {{(render ? "protected override Component? Render() => null;" : string.Empty)}}
                    }
                }
                """),
            new CachedUserAnalyzer());
}
