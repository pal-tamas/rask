using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Rask.Generators.Tests;

// PROTOTYPE — the builder setters carry the propsChanged fold the generated factory computes in one
// shot. The factory can snapshot every prop, assign them all and diff once, because it knows where the
// assignments end; a setter chain does not, so each FOLDING setter accumulates its own delta and the
// parent fires the single NotifyParameters when its Render() returns.
//
// Which props fold has to match the factory exactly (see EmitFactory's foldProps), so these pin both
// halves: a value prop tracks, and Key / delegates / auto-wrapped callbacks do not — folding a fresh
// closure every render would report a change every frame and defeat the render cache outright.
public class BuilderSetterEmissionTests
{
    private const string Src = """
                               using Rask.Core;
                               namespace Demo;
                               public partial class Widget : Component
                               {
                                   public string? Title { get; set; }
                                   public int Count { get; set; }
                                   public System.Action? OnPick { get; set; }
                               }
                               """;

    [Fact]
    public void A_value_setter_folds_into_props_changed()
    {
        var output = Run(Src);

        Assert.Contains(
            "Title(this global::Demo.Widget __c, string? value) "
            + "{ global::Rask.Core.BuilderRuntime.Track(__c, __c.Title, value); "
            + "global::Rask.Core.BuilderRuntime.Written(__c, 0x10000UL); __c.Title = value; return __c; }",
            output,
            StringComparison.Ordinal);
        Assert.Contains("global::Rask.Core.BuilderRuntime.Track(__c, __c.Count, value);", output,
            StringComparison.Ordinal);
    }

    // The reset half. A folding setter also clears its pending bit, because the entry marks every
    // folding prop pending and whatever is still pending when the parent's Render() returns is put
    // back to the factory's default — that is what stops `Widget.Title("x")` on one render and
    // `Widget` on the next from keeping the title. Own props are numbered from OwnPendingBit (16) up,
    // above the range the shared Element/Component surface reserves for itself.
    [Fact]
    public void Own_props_claim_pending_bits_above_the_shared_surface()
    {
        var output = Run(Src);

        Assert.Contains("global::Rask.Core.BuilderRuntime.Written(__c, 0x10000UL);", output,
            StringComparison.Ordinal);
        Assert.Contains("public static void __RaskResetPending_Widget(", output, StringComparison.Ordinal);
        Assert.Contains("__c.Title, null))", output, StringComparison.Ordinal);

        // `int Count` is non-nullable with no initializer — a REQUIRED factory parameter, which has no
        // default for the factory to re-apply either. It claims no bit and is never reset.
        var count = output.Split('\n').Single(l => l.Contains(" Count(this ", StringComparison.Ordinal));
        Assert.DoesNotContain("BuilderRuntime.Written", count, StringComparison.Ordinal);
        Assert.DoesNotContain("__c.Count, default", output, StringComparison.Ordinal);
    }

    // A non-folding prop is defaulted the moment the entry is created instead: it never calls Track, so
    // blanking it early cannot make an unchanged component look dirty.
    [Fact]
    public void A_callback_prop_is_reset_eagerly_rather_than_claiming_a_bit()
    {
        var output = Run(Src);

        var pick = output.Split('\n').Single(l => l.Contains(" Pick(this ", StringComparison.Ordinal));
        Assert.DoesNotContain("BuilderRuntime.Written", pick, StringComparison.Ordinal);
        Assert.Contains("__c.OnPick = null;", EagerReset(output, "Widget"), StringComparison.Ordinal);
    }

    // A prop with a constant member initializer is an OPTIONAL factory param whose default is that
    // value, so the reset restores the initializer — resetting it to null would be a different bug.
    [Fact]
    public void A_prop_with_a_member_initializer_resets_to_the_initializer()
    {
        var output = Run("""
                         using Rask.Core;
                         namespace Demo;
                         public partial class Widget : Component
                         {
                             public string Note { get; set; } = "n/a";
                             public System.Collections.Generic.List<string> Rows { get; set; } = new();
                         }
                         """);

        // Note folds, so it is reset at the END of the render, and against the initializer — never null.
        var pending = PendingReset(output, "Widget");
        Assert.Contains("Equals(__c.Note, \"n/a\")", pending, StringComparison.Ordinal);
        Assert.Contains("__c.Note = \"n/a\";", pending, StringComparison.Ordinal);

        // A NON-constant initializer excludes the prop from the factory parameters entirely, so the
        // factory never re-applies it — and neither may the reset (it still gets a setter).
        Assert.DoesNotContain("__c.Rows", pending, StringComparison.Ordinal);
        Assert.DoesNotContain("__c.Rows", EagerReset(output, "Widget"), StringComparison.Ordinal);
    }

    private static string EagerReset(string output, string type) => Method(output, "__RaskResetEager_" + type);

    private static string PendingReset(string output, string type) => Method(output, "__RaskResetPending_" + type);

    // The generated methods are brace-per-line at a fixed indent, so the body runs to the first "    }".
    private static string Method(string output, string name)
    {
        var start = output.IndexOf(" " + name + "(", StringComparison.Ordinal);
        Assert.True(start >= 0, name + " was not emitted");
        var end = output.IndexOf("\n    }", start, StringComparison.Ordinal);
        return output.Substring(start, end - start);
    }

    // A callback prop is a fresh delegate (and, once wrapped, a fresh closure) on every render, so it
    // is never meaningfully equal to the previous one. The factory leaves it out of the fold; so does
    // the setter, or every callback-taking component would re-render every frame.
    [Fact]
    public void A_callback_setter_does_not_fold()
    {
        var output = Run(Src);

        var line = output.Split('\n').Single(l => l.Contains(" Pick(this ", StringComparison.Ordinal));
        Assert.DoesNotContain("BuilderRuntime.Track", line, StringComparison.Ordinal);
    }

    // Key is a reconciliation identity, not a reactive prop: a changed Key means a different logical
    // item, which mounts fresh rather than firing OnPropsChanged on the old instance.
    [Fact]
    public void The_Key_setter_does_not_fold()
    {
        var src = """
                  using Rask.Core;
                  namespace Demo;
                  public partial class Widget : Component
                  {
                      public object? Key { get; set; }
                  }
                  """;

        var line = Run(src).Split('\n').Single(l => l.Contains(" Key(this ", StringComparison.Ordinal));

        Assert.DoesNotContain("BuilderRuntime.Track", line, StringComparison.Ordinal);
    }

    // Element's ~93 universal props are emitted ONCE as constrained generic extensions rather than
    // per tag, so they take a different branch of EmitSetter. Only the assembly declaring Element
    // contributes them — hence the source-declared Element here.
    [Fact]
    public void The_shared_generic_setters_fold_the_same_way()
    {
        var src = """
                  namespace Rask.Core;
                  public abstract partial class Element : Component
                  {
                      public string? Class { get; set; }
                      public global::Rask.Core.Callback? OnClick { get; set; }
                  }
                  """;

        var output = Run(src);

        Assert.Contains(
            "public static T Class<T>(this T __c, string? value) where T : global::Rask.Core.Element "
            + "{ global::Rask.Core.BuilderRuntime.Track(__c, __c.Class, value); "
            + "global::Rask.Core.BuilderRuntime.Written(__c, 0x1UL); __c.Class = value; return __c; }",
            output,
            StringComparison.Ordinal);

        var click = output.Split('\n').Single(l => l.Contains(" Click<T>(this ", StringComparison.Ordinal));
        Assert.DoesNotContain("BuilderRuntime.Track", click, StringComparison.Ordinal);
    }

    // A carrier prop keeps its own name — that is the whole point of the carrier, and it is what makes
    // Element's event surface read `.OnClick(…)` instead of `.Click(…)`. The SETTER still takes the
    // delegate: a method group cannot reach a carrier, because C# will not chain a delegate conversion
    // into a user-defined one. And it must not be AutoCallback-wrapped — a DOM handler is forwarded
    // raw, where handler-owner resolution already re-renders the owner.
    [Fact]
    public void A_carrier_element_event_keeps_its_name_and_is_not_wrapped()
    {
        var src = """
                  namespace Rask.Core;
                  public abstract partial class Element : Component
                  {
                      public global::Rask.Core.Handler? OnClick { get; set; }
                      public global::Rask.Core.Handler<global::Rask.Core.Live.MouseEventArgs>? OnMouseDown { get; set; }
                      public global::Rask.Core.HandlerAsync? OnClickAsync { get; set; }
                  }
                  """;

        var output = Run(src);

        Assert.Contains(
            "public static T OnClick<T>(this T __c, global::Rask.Core.Callback? value) "
            + "where T : global::Rask.Core.Element "
            + "{ __c.OnClick = new global::Rask.Core.Handler(value); return __c; }",
            output,
            StringComparison.Ordinal);
        Assert.Contains(
            "OnMouseDown<T>(this T __c, "
            + "global::Rask.Core.Callback<global::Rask.Core.Live.MouseEventArgs>? value)",
            output,
            StringComparison.Ordinal);
        Assert.Contains(
            "OnClickAsync<T>(this T __c, global::Rask.Core.CallbackAsync? value)",
            output,
            StringComparison.Ordinal);
        Assert.DoesNotContain("AutoCallback.Wrap", output, StringComparison.Ordinal);
    }

    // A prop inherited from an INTERMEDIATE base (HtmlMediaElement, BsBlock, BsFormControl<T>, a
    // consumer's own base) has no shared emission to fall back on — only Rask.Core's Element/Component
    // chain is written once as constrained generics — so it needs a per-component setter or it gets
    // none at all. The receiver is the concrete component, so the chain keeps its type.
    [Fact]
    public void A_prop_from_an_intermediate_base_gets_a_setter_on_the_concrete_component()
    {
        var output = Run("""
                         using Rask.Core;
                         namespace Demo;
                         public abstract class Panel : Component
                         {
                             public string? Heading { get; set; }
                         }

                         public partial class Widget : Panel
                         {
                             public int Count { get; set; }
                         }
                         """);

        Assert.Contains("Heading(this global::Demo.Widget __c, string? value)", output, StringComparison.Ordinal);

        // …and the inherited prop is reset like an own one, or the chain would keep last render's
        // heading where the factory would have put it back.
        Assert.Contains("__c.Heading, null))", PendingReset(output, "Widget"), StringComparison.Ordinal);

        // Component's own props stay on the shared surface: emitting them here as well would be dead
        // weight on every component in the assembly.
        Assert.DoesNotContain("Key(this global::Demo.Widget", output, StringComparison.Ordinal);
    }

    // The shared Element/Component surface gets its reset emitted ONCE, into Rask.Core.BuilderRuntime —
    // a fixed name, because a consumer assembly cannot know the per-assembly setter class Rask.Core
    // emitted its own setters into. Only the assembly declaring Element contributes it.
    [Fact]
    public void The_shared_surface_reset_is_emitted_once_onto_BuilderRuntime()
    {
        var output = Run("""
                         namespace Rask.Core;
                         public abstract partial class Element : Component
                         {
                             public string? Class { get; set; }
                             public global::Rask.Core.Callback? OnClick { get; set; }
                         }
                         """, "RaskBuilderReset.g.cs");

        Assert.Contains("public static partial class BuilderRuntime", output, StringComparison.Ordinal);
        Assert.Contains("public static void ResetElementEager(global::Rask.Core.Component __c0)", output,
            StringComparison.Ordinal);
        Assert.Contains("__c.OnClick = null;", output, StringComparison.Ordinal);
        Assert.Contains("public const ulong SharedElementPending = 0x1UL;", output, StringComparison.Ordinal);

        // Key is Component's, not Element's, and never folds — so it is reset eagerly one level up.
        Assert.Contains("__c.Key = null;", output, StringComparison.Ordinal);
        Assert.Contains("public const ulong SharedComponentPending = 0x0UL;", output, StringComparison.Ordinal);
    }

    private static string Run(string source, string hintName = "RaskBuilderSetters.g.cs")
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Latest));
        var compilation = CSharpCompilation.Create(
            "TestAssembly",
            new[] { syntaxTree },
            GeneratorDriverFixture.BuildReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable));

        var driver = (CSharpGeneratorDriver)CSharpGeneratorDriver
            .Create(new ComponentFactoryGenerator())
            .WithUpdatedParseOptions(new CSharpParseOptions(LanguageVersion.Latest))
            .WithUpdatedAnalyzerConfigOptions(new BuilderSurfaceOptionsProvider());

        var generated = driver.RunGenerators(compilation).GetRunResult()
            .Results.SelectMany(r => r.GeneratedSources).ToImmutableArray();

        var match = generated.FirstOrDefault(s => s.HintName.Contains(hintName, StringComparison.Ordinal));
        if (match.SourceText is null)
        {
            throw new InvalidOperationException(
                $"No {hintName} generated. Available: ["
                + string.Join(", ", generated.Select(s => s.HintName)) + "]");
        }

        return match.SourceText.ToString();
    }

    // The builder surface is opt-in (RaskBuilderSurface), so the driver has to say so.
    private sealed class BuilderSurfaceOptionsProvider : AnalyzerConfigOptionsProvider
    {
        public override AnalyzerConfigOptions GlobalOptions { get; } = new Options();

        public override AnalyzerConfigOptions GetOptions(SyntaxTree tree) => GlobalOptions;

        public override AnalyzerConfigOptions GetOptions(AdditionalText textFile) => GlobalOptions;

        private sealed class Options : AnalyzerConfigOptions
        {
            public override bool TryGetValue(string key, out string value)
            {
                if (key == "build_property.RaskBuilderSurface")
                {
                    value = "true";
                    return true;
                }

                value = string.Empty;
                return false;
            }
        }
    }
}
