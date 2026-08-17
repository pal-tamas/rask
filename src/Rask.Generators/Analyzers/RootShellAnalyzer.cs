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
///     RASK021 — flags a root component that renders the page shell itself.
///     <para>
///         The component passed to <c>UseRask&lt;TApp&gt;()</c> (Server / Wasm.Hosting) or
///         <c>RunAsync&lt;TApp&gt;()</c> (standalone WASM) renders straight into <c>&lt;body&gt;</c>:
///         Rask emits the doctype, <c>&lt;html&gt;</c>, <c>&lt;head&gt;</c> and <c>&lt;body&gt;</c>
///         around whatever it returns. A root that still builds them itself nests a second document
///         inside the body, which the HTML parser then silently unwraps — a page that looks nearly
///         right and has lost its attributes. Since nothing fails, the compile-time signal is the
///         only one there is (the runtime backstop this diagnostic used to pair with is gone: the
///         shell is no longer the app's to get wrong).
///     </para>
///     <para>
///         Best-effort: only fires when <c>TApp</c> and its <c>Render()</c> are visible in the
///         current compilation's source, and matches the shell factories by name (the names the
///         user writes). Apps composed via a delegated helper or pulled from a referenced
///         assembly aren't analyzed. Warning severity, so it never breaks a build and is
///         suppressible per call site.
///     </para>
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class RootShellAnalyzer : DiagnosticAnalyzer
{
    private const string RaskCoreAssembly = "Rask.Core";

    private const string ServerExtensions = "Rask.Server.RaskEndpointExtensions";
    private const string WasmHostingExtensions = "Rask.Wasm.Hosting.RaskWasmEndpointExtensions";
    private const string WasmHostBuilder = "Rask.Wasm.WasmHostBuilder";

    // Shell factory names the framework now owns, in canonical document order. Matched by the invoked
    // method's simple name — these are the generated factory names the user writes (Doctype(),
    // Html(...), Head(), Body()).
    private static readonly string[] _shellFactories = { "Doctype", "Html", "Head", "Body" };

    private static readonly DiagnosticDescriptor Rask021 = new(
        "RASK021",
        "Root component must not render the page shell",
        "The Rask root component '{0}' renders the page shell itself ({1}). Rask builds the document around the root — return the body's content and move <head> contributions to the Head override, <html lang> to HtmlLang, <body class> to BodyClass, or the whole document to a Shell override.",
        DiagnosticHelp.Category,
        DiagnosticSeverity.Warning,
        true,
        description: "The root component renders into <body>: Rask emits the doctype, <html>, <head> and <body> around "
                     + "whatever it returns. Building them again nests a second document inside the body, which the "
                     + "parser unwraps — the page keeps rendering and quietly loses the nested tags' attributes. Return "
                     + "the body content (typically Router()); put head contributions in the Head override, the "
                     + "document language in HtmlLang, the body class in BodyClass, and anything else in a "
                     + "Shell(head, body) override. Do not add the runtime <script> — it is appended to <body> "
                     + "automatically.",
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
        foreach (var node in renderBody.DescendantNodes())
        {
            // The factory spelling: `Html("en")`, `Head()`. The chain writes the same shell without ever
            // invoking anything — `Html[ … ]` is an element access, and `Doctype` on its own is a bare
            // identifier — so a scan over invocations alone saw none of it.
            var name = node switch
            {
                InvocationExpressionSyntax invocation => InvokedSimpleName(invocation.Expression),
                ElementAccessExpressionSyntax element => InvokedSimpleName(element.Expression),
                IdentifierNameSyntax id when IsStandaloneValue(id) => id.Identifier.ValueText,
                _ => null,
            };

            if (name is not null && Array.IndexOf(_shellFactories, name) >= 0)
            {
                produced.Add(name);
            }
        }

        if (produced.Count == 0)
        {
            return;
        }

        // Report in canonical document order rather than discovery order, so the message reads the way
        // the shell is written and is stable across edits that only move things around.
        List<string>? found = null;
        foreach (var factory in _shellFactories)
        {
            if (produced.Contains(factory))
            {
                (found ??= new List<string>()).Add(factory + "()");
            }
        }

        context.ReportDiagnostic(Diagnostic.Create(
            Rask021, op.Syntax.GetLocation(), app.Name, string.Join(", ", found!)));
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
    // A bare entry used as a value — `Doctype` standing alone. Excludes the identifier that merely NAMES
    // something larger (the receiver of a call or an indexer, the right-hand side of a member access, a
    // declaration), which the arms above already account for; counting those too would report the same
    // shell twice and, worse, fire on any local that happens to be called Body.
    private static bool IsStandaloneValue(IdentifierNameSyntax id) => id.Parent switch
    {
        InvocationExpressionSyntax invocation => invocation.Expression != id,
        ElementAccessExpressionSyntax element => element.Expression != id,
        MemberAccessExpressionSyntax => false,
        QualifiedNameSyntax => false,
        _ => true,
    };

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
