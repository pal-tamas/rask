using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Rask.Generators.Analyzers;

/// <summary>
///     RASK021 — flags a root component that does not render the full page shell.
///     <para>
///         The component passed to <c>UseRask&lt;TApp&gt;()</c> (Server / Wasm.Hosting) or
///         <c>RunAsync&lt;TApp&gt;()</c> (standalone WASM) is the document root: its
///         <c>Render()</c> must produce <c>Doctype()</c>, <c>Html(...)</c>, <c>Head()</c>, and
///         <c>Body()</c>. Omitting any of them yields a broken page (and, without a
///         <c>&lt;body&gt;</c>, nowhere for the auto-injected runtime script to land). We surface
///         the gap at compile time; the framework also fails fast at runtime as a backstop.
///     </para>
///     <para>
///         Best-effort: only fires when <c>TApp</c> and its <c>Render()</c> are visible in the
///         current compilation's source, and matches the shell factories by name (the names the
///         user writes). Apps composed via a delegated helper or pulled from a referenced
///         assembly aren't analyzed — the runtime check covers those. Warning severity, so it
///         never breaks a build and is suppressible per call site.
///     </para>
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class RootShellAnalyzer : DiagnosticAnalyzer
{
    private const string RaskCoreAssembly = "Rask.Core";

    private const string ServerExtensions = "Rask.Server.RaskEndpointExtensions";
    private const string WasmHostingExtensions = "Rask.Wasm.Hosting.RaskWasmEndpointExtensions";
    private const string WasmHostBuilder = "Rask.Wasm.WasmHostBuilder";

    // Shell factory names a root Render() must call, in canonical document order. Matched by the
    // invoked method's simple name — these are the generated factory names the user writes
    // (Doctype(), Html(...), Head(), Body()).
    private static readonly string[] _shellFactories = { "Doctype", "Html", "Head", "Body" };

    private static readonly DiagnosticDescriptor Rask021 = new(
        "RASK021",
        "Root component must render a complete page shell",
        "The Rask root component '{0}' does not render a complete page shell; missing: {1}. A root Render() should produce Doctype(), Html(...)[Head(), Body()[...]].",
        DiagnosticHelp.Category,
        DiagnosticSeverity.Warning,
        true,
        description: "The root component renders the whole document, so it must produce the shell itself: Doctype(), "
                     + "then Html(…)[ Head(), Body()[ … ] ]. A runtime backstop enforces the same thing, so a shell that "
                     + "slips past this analyzer fails at render instead. Do not add the runtime <script> — it is "
                     + "appended to <body> automatically.",
        helpLinkUri: DiagnosticHelp.Link("RASK021"));

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(Rask021);

    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.RegisterCompilationStartAction(static start =>
        {
            if (string.Equals(start.Compilation.AssemblyName, RaskCoreAssembly, StringComparison.Ordinal))
            {
                return;
            }

            start.RegisterOperationAction(Analyze, OperationKind.Invocation);
        });
    }

    private static void Analyze(OperationAnalysisContext context)
    {
        var op = (IInvocationOperation)context.Operation;
        var method = op.TargetMethod;

        if (!IsEntryPoint(method) || method.TypeArguments.Length != 1)
        {
            return;
        }

        if (method.TypeArguments[0] is not INamedTypeSymbol app)
        {
            return;
        }

        // Locate the App's Render() in source (own declaration or a source base). If it lives
        // only in metadata, we can't inspect its body — leave it to the runtime check.
        var renderBody = FindRenderBodyInSource(app);
        if (renderBody is null)
        {
            return;
        }

        var produced = new HashSet<string>(StringComparer.Ordinal);
        foreach (var invocation in renderBody.DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            var name = InvokedSimpleName(invocation.Expression);
            if (name is not null && Array.IndexOf(_shellFactories, name) >= 0)
            {
                produced.Add(name);
            }
        }

        List<string>? missing = null;
        foreach (var factory in _shellFactories)
        {
            if (!produced.Contains(factory))
            {
                (missing ??= new List<string>()).Add(factory + "()");
            }
        }

        if (missing is null)
        {
            return;
        }

        context.ReportDiagnostic(Diagnostic.Create(
            Rask021, op.Syntax.GetLocation(), app.Name, string.Join(", ", missing)));
    }

    private static bool IsEntryPoint(IMethodSymbol method)
    {
        var containing = method.ContainingType?.ToDisplayString();
        return method.Name switch
        {
            "UseRask" => containing is ServerExtensions or WasmHostingExtensions,
            "RunAsync" => containing == WasmHostBuilder,
            _ => false
        };
    }

    // Simple name of an invoked expression: `Doctype()` → "Doctype",
    // `Generated.Html(...)` → "Html", `Foo<T>()` → "Foo".
    private static string? InvokedSimpleName(ExpressionSyntax expression) => expression switch
    {
        IdentifierNameSyntax id => id.Identifier.ValueText,
        GenericNameSyntax g => g.Identifier.ValueText,
        MemberAccessExpressionSyntax m => m.Name.Identifier.ValueText,
        _ => null
    };

    // Walk the type hierarchy for a parameterless Render() declared in source; returns the
    // method body / arrow-expression node to scan, or null if Render is only in metadata.
    private static SyntaxNode? FindRenderBodyInSource(INamedTypeSymbol app)
    {
        for (var t = app; t is not null; t = t.BaseType)
        {
            var render = t.GetMembers("Render")
                .OfType<IMethodSymbol>()
                .FirstOrDefault(m => !m.IsStatic && m.Parameters.Length == 0);
            if (render is null)
            {
                continue;
            }

            return render.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax() is MethodDeclarationSyntax decl
                ? (SyntaxNode?)decl.Body ?? decl.ExpressionBody
                : null;
        }

        return null;
    }
}
