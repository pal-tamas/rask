using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Rask.Generators.Analyzers;

/// <summary>
///     RASK121 — a <c>[JSInvokable]</c> method that is not <c>public</c>.
///     <para>
///         Both hosts answer the browser's <c>DotNet.invokeMethodAsync</c> through <c>Microsoft.JSInterop</c>'s
///         dispatcher, which looks only at public methods: a private or internal one compiles and is then "not
///         found" the first time the browser calls it. Rask's counterpart of Blazor's BL0015, for the projects
///         that one does not reach — a WebAssembly app and a library on <c>Rask</c> alone. Where the ASP.NET
///         components analyzers are already loaded (a project that sees <c>ComponentBase</c>: a Server app),
///         BL0015 says the same thing and this stays quiet, so a method is never reported twice.
///     </para>
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class NonPublicJSInvokableAnalyzer : DiagnosticAnalyzer
{
    private const string JSInvokableFullName = "Microsoft.JSInterop.JSInvokableAttribute";

    // Part of the ASP.NET shared framework and of the Microsoft.AspNetCore.Components package, both of which
    // bring Microsoft.AspNetCore.Components.Analyzers — and with it BL0015.
    private const string ComponentBaseFullName = "Microsoft.AspNetCore.Components.ComponentBase";

    private static readonly DiagnosticDescriptor Rask121 = new(
        "RASK121",
        "A [JSInvokable] method must be public",
        "'{0}' is [JSInvokable] but not public: the browser's call reaches only public methods, so it fails at run "
        + "time; make it public",
        DiagnosticHelp.Category,
        DiagnosticSeverity.Warning,
        true,
        "Microsoft.JSInterop's dispatcher, which both Rask hosts answer DotNet.invokeMethodAsync with, finds only "
        + "public methods. A [JSInvokable] that is private, protected or internal compiles, and the browser's call "
        + "to it fails.",
        DiagnosticHelp.Link("RASK121"));

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(Rask121);

    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.RegisterCompilationStartAction(static start =>
        {
            if (start.Compilation.GetTypeByMetadataName(ComponentBaseFullName) is not null
                || start.Compilation.GetTypeByMetadataName(JSInvokableFullName) is not { } attribute)
            {
                return;
            }

            start.RegisterSymbolAction(ctx => Analyze(ctx, attribute), SymbolKind.Method);
        });
    }

    private static void Analyze(SymbolAnalysisContext context, INamedTypeSymbol attribute)
    {
        var method = (IMethodSymbol)context.Symbol;
        if (method.DeclaredAccessibility == Accessibility.Public
            || !method.GetAttributes().Any(a => SymbolEqualityComparer.Default.Equals(a.AttributeClass, attribute))
            || method.Locations.FirstOrDefault() is not { IsInSource: true } location)
        {
            return;
        }

        context.ReportDiagnostic(Diagnostic.Create(Rask121, location, method.Name));
    }
}
