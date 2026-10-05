using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Rask.Generators.Analyzers;

namespace Rask.Generators.Tests;

public class BrowserTargetsAnalyzerTests
{
    // Shaped like Rask.Web's generated proxies, and compiled to METADATA first: the analyzer reads the attribute off a
    // referenced assembly's symbols, never off source, so that is the path worth pinning. The attribute is internal,
    // as Rask.Core's is.
    private const string Library = """
        using System.Threading.Tasks;

        namespace Rask.Core
        {
            [System.AttributeUsage(System.AttributeTargets.Method | System.AttributeTargets.Property)]
            internal sealed class BrowserSupportAttribute : System.Attribute
            {
                public string? Chrome { get; set; }
                public string? Firefox { get; set; }
                public string? Safari { get; set; }
            }
        }

        namespace Web
        {
            public sealed class Usb
            {
                public ValueTask<bool> IsSupported => new(false);

                [global::Rask.Core.BrowserSupport(Chrome = "61")]
                public ValueTask<UsbDevice> RequestDevice() => default;

                [global::Rask.Core.BrowserSupport(Chrome = "37", Firefox = "98", Safari = "15.4")]
                public ValueTask<string> Label => default;
            }

            public sealed class UsbDevice
            {
                [global::Rask.Core.BrowserSupport(Chrome = "61")]
                public ValueTask Open() => default;
            }

            public static class Navigator
            {
                public static Usb Usb { get; } = new();
            }

            public sealed class Dialog { }

            public static class DialogMembers
            {
                extension(Dialog dialog)
                {
                    [global::Rask.Core.BrowserSupport(Chrome = "114", Firefox = "120")]
                    public ValueTask RequestClose() => default;
                }
            }
        }
        """;

    private static readonly Lazy<MetadataReference> LibraryReference = new(CompileLibrary);

    private static string App(string body) => $$"""
        using System.Threading.Tasks;
        using Web;

        public static class App
        {
            public static async Task Run(Dialog dialog)
            {
                {{body}}
            }
        }
        """;

    [Fact]
    public async Task A_member_a_target_browser_never_shipped_is_reported_as_RASK098()
    {
        var source = App("await Navigator.Usb.RequestDevice();");

        var d = Assert.Single(await Diagnostics(source, "safari >= 16; firefox >= 115"));

        Assert.Equal("RASK098", d.Id);
        Assert.Contains("'RequestDevice' is not in Safari >= 16 (never shipped), Firefox >= 115 (never shipped)", d.GetMessage(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_member_shipped_after_the_target_version_is_reported_with_the_version_it_came_in()
    {
        var source = App("await dialog.RequestClose();");

        var d = Assert.Single(await Diagnostics(source, "firefox >= 115"));

        Assert.Contains("'RequestClose' is not in Firefox >= 115 (from 120)", d.GetMessage(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_member_every_target_ships_is_not_reported()
    {
        var source = App("await Navigator.Usb.Label; await dialog.RequestClose();");

        var found = await Diagnostics(source, "firefox >= 120; chrome >= 114; edge >= 120");

        Assert.Empty(found);
    }

    [Fact]
    public async Task A_version_compares_by_number_not_by_text()
    {
        var source = App("await Navigator.Usb.Label;");

        var found = await Diagnostics(source, "safari >= 15.10");

        Assert.Empty(found);
    }

    [Fact]
    public async Task A_call_inside_an_IsSupported_guard_is_not_reported()
    {
        var source = App("""
            if (await Navigator.Usb.IsSupported)
            {
                var device = await Navigator.Usb.RequestDevice();
                await device.Open();
            }
            """);

        var found = await Diagnostics(source, "safari >= 16");

        Assert.Empty(found);
    }

    [Fact]
    public async Task A_call_after_an_early_exit_on_IsSupported_is_not_reported()
    {
        var source = App("""
            if (!await Navigator.Usb.IsSupported)
            {
                return;
            }

            await Navigator.Usb.RequestDevice();
            """);

        var found = await Diagnostics(source, "safari >= 16");

        Assert.Empty(found);
    }

    [Fact]
    public async Task A_call_in_the_else_of_an_IsSupported_guard_is_still_reported()
    {
        var source = App("""
            if (await Navigator.Usb.IsSupported) { }
            else { await Navigator.Usb.RequestDevice(); }
            """);

        var found = await Diagnostics(source, "safari >= 16");

        Assert.Single(found);
    }

    [Fact]
    public async Task Nothing_is_reported_when_the_property_is_unset()
    {
        var source = App("await Navigator.Usb.RequestDevice();");

        var found = await Diagnostics(source, targets: null);

        Assert.Empty(found);
    }

    [Fact]
    public async Task An_entry_that_is_not_a_browser_target_is_reported_once_as_RASK099()
    {
        var source = App("await Navigator.Usb.Label;");

        var d = Assert.Single(await Diagnostics(source, "safari >= 16; opera 90"));

        Assert.Equal("RASK099", d.Id);
        Assert.Contains("'opera 90'", d.GetMessage(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_real_Rask_Core_element_ref_member_carries_its_support()
    {
        // The seam: the emitter writes the attribute on Rask.Core's generated element-ref members, and the analyzer
        // reads it through the C# 14 extension block as an app calls it. virtualKeyboardPolicy is Chromium's alone.
        var source = """
            using System.Threading.Tasks;
            using Rask.Core;

            public static class App
            {
                public static async Task Run(ElementRef<HTMLDivElement> div) => await div.SetVirtualKeyboardPolicy("manual");
            }
            """;

        var d = Assert.Single(await Diagnostics(source, "safari >= 17", withLibrary: false));

        Assert.Contains("'SetVirtualKeyboardPolicy' is not in Safari >= 17 (never shipped)", d.GetMessage(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_attribute_keyword_a_target_lacks_is_reported_where_it_is_named()
    {
        // popover="hint" is Chromium's and Firefox's: the emitter writes its support on the enum member itself.
        var source = """
            using Rask.Core;

            public static class App
            {
                public static Popover Hint() => Popover.Hint;
            }
            """;

        var d = Assert.Single(await Diagnostics(source, "safari >= 17", withLibrary: false));

        Assert.Contains("'Hint' is not in Safari >= 17 (never shipped)", d.GetMessage(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_attribute_keyword_with_no_support_of_its_own_is_not_reported()
    {
        var source = """
            using Rask.Core;

            public static class App
            {
                public static Popover Auto() => Popover.Auto;
            }
            """;

        var found = await Diagnostics(source, "safari >= 17", withLibrary: false);

        Assert.Empty(found);
    }

    private static MetadataReference CompileLibrary()
    {
        var compilation = CSharpCompilation.Create(
            "Web",
            [CSharpSyntaxTree.ParseText(Library, new CSharpParseOptions(LanguageVersion.Preview))],
            GeneratorDriverFixture.BuildReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));
        using var stream = new MemoryStream();
        var emitted = compilation.Emit(stream);
        Assert.True(emitted.Success, string.Join("\n", emitted.Diagnostics));
        return MetadataReference.CreateFromImage(stream.ToArray());
    }

    private static async Task<ImmutableArray<Diagnostic>> Diagnostics(string source, string? targets, bool withLibrary = true)
    {
        var references = withLibrary
            ? GeneratorDriverFixture.BuildReferences().Add(LibraryReference.Value)
            : GeneratorDriverFixture.BuildReferences();
        var compilation = CSharpCompilation.Create(
            "TestApp",
            [CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview))],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));
        var options = new AnalyzerOptions([], new GlobalOptionsProvider(targets));

        var all = await compilation
            .WithAnalyzers([new BrowserTargetsAnalyzer()], options)
            .GetAnalyzerDiagnosticsAsync();
        return [.. all.Where(d => d.Id is "RASK098" or "RASK099")];
    }

    private sealed class GlobalOptionsProvider(string? targets) : AnalyzerConfigOptionsProvider
    {
        public override AnalyzerConfigOptions GlobalOptions { get; } = new Options(targets);

        public override AnalyzerConfigOptions GetOptions(SyntaxTree tree) => GlobalOptions;

        public override AnalyzerConfigOptions GetOptions(AdditionalText textFile) => GlobalOptions;

        private sealed class Options(string? targets) : AnalyzerConfigOptions
        {
            public override bool TryGetValue(string key, [NotNullWhen(true)] out string? value)
            {
                value = key == "build_property.RaskBrowserTargets" ? targets : null;
                return value is not null;
            }
        }
    }
}
