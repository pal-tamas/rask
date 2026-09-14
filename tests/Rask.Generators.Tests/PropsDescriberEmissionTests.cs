namespace Rask.Generators.Tests;

// The devtools Tree tab shows what each component was given, and this is where those values come from: an
// override of Component.DescribeProps written by the build, reading the component's own properties by name.
// No reflection, so a trimmed app describes as much as a JIT one — and nothing to strip in Release, because
// the emission is gated on the RaskDevTools build property and a build that never asks gets no override at all.
//
// Two behaviours cannot be demonstrated by the kit, which is what these pin: the gate (Rask.Ui builds with the
// property ON, so its overrides only ever prove the ON half), and redaction (no component in the kit has a
// password-ish property, so nothing there would ever take the redacting branch).
public class PropsDescriberEmissionTests
{
    // Every attribute is written out in full. `using System.ComponentModel;` would put its own Component in
    // scope beside Rask.Core's, and `class Login : Component` is then CS0104 — the class binds to neither, the
    // generator sees no component at all, and a test written that way passes or fails for reasons of its own.
    private const string Src = """
                               using Rask.Core;
                               namespace Demo;
                               public partial class Login : Component
                               {
                                   public string? User { get; set; }
                                   public int Attempts { get; set; }
                                   public string? Password { get; set; }

                                   [System.ComponentModel.DataAnnotations.DataType(
                                       System.ComponentModel.DataAnnotations.DataType.Password)]
                                   public string? Shibboleth { get; set; }

                                   [System.ComponentModel.PasswordPropertyText]
                                   public string? Memorable { get; set; }
                               }
                               """;

    // The gate. An absent property means off — a Release build, or any build that never asked for the tools,
    // describes nothing about an app's own state. Asserted over the whole run, not just the one file: an
    // override emitted into some other generated file would describe props just as well.
    [Fact]
    public void A_build_that_did_not_ask_for_the_devtools_describes_nothing()
    {
        var run = BuilderGeneratorHarness.Run(Src);

        Assert.DoesNotContain(run.Sources, s => s.HintName.Contains("PropsDescribers", StringComparison.Ordinal));
        Assert.DoesNotContain(run.Sources,
            s => s.SourceText.ToString().Contains("DescribeProps", StringComparison.Ordinal));
    }

    [Fact]
    public void The_describer_defers_to_the_base_and_then_reads_each_prop()
    {
        var output = BuilderGeneratorHarness.Run(Src, devTools: true).Source("RaskPropsDescribers");

        // The base call first: a component's own override describes only what it declares, and the shared
        // Element/Component surface underneath it is described by Core's own overrides.
        Assert.Contains("base.DescribeProps(describer);", output, StringComparison.Ordinal);

        // `protected override`, not `protected internal override`: TestAssembly is not the assembly that
        // declares Component, and keeping the internal half outside it is CS0507.
        Assert.Contains("protected override void DescribeProps(", output, StringComparison.Ordinal);

        Assert.Contains(
            "global::Rask.Core.Diagnostics.DevTools.PropsDescriber.Format(this.User));",
            output,
            StringComparison.Ordinal);
        Assert.Contains(
            "global::Rask.Core.Diagnostics.DevTools.PropsDescriber.Format(this.Attempts));",
            output,
            StringComparison.Ordinal);
    }

    // Core names about twenty friend assemblies — the hosts, the islands, Rask.Testing and their test projects —
    // and in every one of them the internal half of `protected internal` IS visible, so the override has to keep
    // both words. This is how the rule surfaced: the devtools' own test project is one of them, and the first
    // component there with a property failed to compile.
    [Fact]
    public void A_friend_of_core_keeps_the_internal_half_of_the_modifier()
    {
        var output = BuilderGeneratorHarness
            .Run(Src, devTools: true, assemblyName: "Rask.DevTools.Tests")
            .Source("RaskPropsDescribers");

        Assert.Contains("protected internal override void DescribeProps(", output, StringComparison.Ordinal);
    }

    // Redaction is a GENERATION-time decision: the value is never read, so there is no moment at which a
    // password sits in a describer waiting to be filtered out on its way to the panel.
    [Theory]
    [InlineData("Password")] // by name
    [InlineData("Shibboleth")] // by [DataType(DataType.Password)]
    [InlineData("Memorable")] // by [PasswordPropertyText]
    public void A_sensitive_prop_is_named_but_never_read(string prop)
    {
        var output = BuilderGeneratorHarness.Run(Src, devTools: true).Source("RaskPropsDescribers");

        Assert.Contains("describer.AddRedacted(\"" + prop + "\", ", output, StringComparison.Ordinal);
        Assert.DoesNotContain("Format(this." + prop + ")", output, StringComparison.Ordinal);
    }
}
