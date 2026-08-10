using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace Rask.Tools.BuilderRewrite;

internal enum SiteVerdict
{
    Convertible,
    FormExcluded,
    NotInMarkupHost,
    GenericFactory,
    NoSetter,
    CompilerRejected,
}

internal sealed record Site(
    int Id,
    InvocationExpressionSyntax Node,
    string ComponentName,
    string File,
    int Line,
    SiteVerdict Verdict,
    string? Detail,
    IReadOnlyList<string> Setters,
    bool NeedsInjectedEntry);

/// <summary>
///     Rewrites one file's factory call sites into setter chains, then asks the compiler whether it was
///     right and un-does whatever it rejects.
/// </summary>
/// <remarks>
///     The verification loop is the point. Resolving a call site against the real factory signature gets
///     the parameter names right, and the setter index gets the method names right, but neither can rule
///     out a shadowed entry, a non-<c>partial</c> host, or a factory call standing where an expression
///     statement has to be. So every rewritten site carries a <see cref="SyntaxAnnotation" />, the
///     rewritten tree goes back into the compilation, and each resulting error is walked up to the site
///     that caused it. An error that cannot be attributed to a site abandons the whole file — better a
///     named gap than a silent one.
/// </remarks>
internal sealed class FileRewriter
{
    private const string AnnotationKind = "rask-builder-site";

    private readonly SurfaceModel _surface;
    private readonly CSharpCompilation _compilation;

    public FileRewriter(CSharpCompilation compilation, SurfaceModel surface)
    {
        _compilation = compilation;
        _surface = surface;
    }

    public sealed record FileResult(
        SyntaxTree? Rewritten,
        IReadOnlyList<Site> Sites,
        IReadOnlyList<int> Converted,
        string? Bailout);

    public FileResult Rewrite(SyntaxTree tree)
    {
        var model = _compilation.GetSemanticModel(tree);
        var sites = Collect(tree, model);
        var convertible = sites.Where(s => s.Verdict == SiteVerdict.Convertible).Select(s => s.Id).ToHashSet();
        if (convertible.Count == 0)
        {
            return new FileResult(null, sites, Array.Empty<int>(), null);
        }

        var byNode = sites.ToDictionary(s => s.Node);
        var baseline = ErrorIds(model.GetDiagnostics().Concat(tree.GetDiagnostics()));

        for (var round = 0; round < 6; round++)
        {
            var rewritten = Apply(tree, byNode, convertible);
            var candidate = _compilation.ReplaceSyntaxTree(tree, rewritten);
            var errors = candidate.GetSemanticModel(rewritten).GetDiagnostics()
                .Concat(rewritten.GetDiagnostics())
                .Where(d => d.Severity == DiagnosticSeverity.Error)
                .ToList();

            var root = rewritten.GetRoot();
            var rejected = new HashSet<int>();
            var unattributable = new List<Diagnostic>();
            foreach (var error in errors)
            {
                if (baseline.Contains(Key(error)))
                {
                    continue;
                }

                var node = root.FindNode(error.Location.SourceSpan, false, true);
                var owner = node.AncestorsAndSelf()
                    .SelectMany(n => n.GetAnnotations(AnnotationKind))
                    .FirstOrDefault();
                if (owner?.Data is { } data && int.TryParse(data, out var id))
                {
                    rejected.Add(id);
                }
                else
                {
                    unattributable.Add(error);
                }
            }

            if (unattributable.Count > 0)
            {
                var first = unattributable[0];
                return new FileResult(
                    null, sites, Array.Empty<int>(),
                    $"{first.Id} {first.GetMessage()} at {first.Location.GetLineSpan().StartLinePosition.Line + 1}");
            }

            if (rejected.Count == 0)
            {
                return new FileResult(rewritten, sites, convertible.ToList(), null);
            }

            foreach (var id in rejected)
            {
                convertible.Remove(id);
            }

            sites = sites.Select(s => rejected.Contains(s.Id)
                ? s with { Verdict = SiteVerdict.CompilerRejected, Detail = "compiler rejected the chain" }
                : s).ToList();
            byNode = sites.ToDictionary(s => s.Node);

            if (convertible.Count == 0)
            {
                return new FileResult(null, sites, Array.Empty<int>(), null);
            }
        }

        return new FileResult(null, sites, Array.Empty<int>(), "did not converge in 6 rounds");
    }

    private static HashSet<string> ErrorIds(IEnumerable<Diagnostic> diagnostics) =>
        diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Select(Key).ToHashSet(StringComparer.Ordinal);

    private static string Key(Diagnostic d) => d.Id + "|" + d.GetMessage();

    // ---- collection ------------------------------------------------------------------------------

    private List<Site> Collect(SyntaxTree tree, SemanticModel model)
    {
        var sites = new List<Site>();
        var id = 0;

        foreach (var node in tree.GetRoot().DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            if (model.GetSymbolInfo(node).Symbol is not IMethodSymbol method || !_surface.IsFactory(method))
            {
                continue;
            }

            var component = (INamedTypeSymbol)method.ReturnType;
            var siteId = id++;
            var name = component.Name;
            var line = tree.GetLineSpan(node.Span).StartLinePosition.Line + 1;
            var file = tree.FilePath;

            Site Make(SiteVerdict verdict, string? detail, IReadOnlyList<string>? setters = null) =>
                new(siteId, node, name, file, line, verdict, detail, setters ?? Array.Empty<string>(),
                    !SymbolEqualityComparer.Default.Equals(
                        component.ContainingAssembly, _surface.FrameworkAssembly));

            // `Form<TModel>` is pending its own work and every one of its call sites will move again
            // when it lands, so it is out of this migration entirely.
            if (name is "Form")
            {
                sites.Add(Make(SiteVerdict.FormExcluded, null));
                continue;
            }

            if (method.IsGenericMethod || component.IsGenericType)
            {
                // A generic component's entry is a METHOD, and a method entry displaces its same-named
                // factory inside a markup host — so these sites do not sit still while the surface moves
                // under them. They move with the entry, not here.
                sites.Add(Make(SiteVerdict.GenericFactory, "generic component"));
                continue;
            }

            if (!_surface.IsInMarkupHost(SurfaceModel.EnclosingType(model, node)))
            {
                sites.Add(Make(SiteVerdict.NotInMarkupHost, null));
                continue;
            }

            var setters = new List<string>();
            string? missing = null;
            foreach (var argument in node.ArgumentList.Arguments)
            {
                var parameter = ParameterFor(method, node, argument);
                if (parameter is null)
                {
                    missing = "?";
                    break;
                }

                var setter = _surface.SetterFor(component, parameter);
                if (setter is null)
                {
                    missing = parameter.Name;
                    break;
                }

                setters.Add(setter);
            }

            sites.Add(missing is null
                ? Make(SiteVerdict.Convertible, null, setters)
                : Make(SiteVerdict.NoSetter, missing));
        }

        return sites;
    }

    private static IParameterSymbol? ParameterFor(
        IMethodSymbol method, InvocationExpressionSyntax node, ArgumentSyntax argument)
    {
        if (argument.NameColon is { } named)
        {
            return method.Parameters.FirstOrDefault(p => p.Name == named.Name.Identifier.ValueText);
        }

        var index = node.ArgumentList.Arguments.IndexOf(argument);
        return index >= 0 && index < method.Parameters.Length ? method.Parameters[index] : null;
    }

    // ---- application -----------------------------------------------------------------------------

    private static SyntaxTree Apply(
        SyntaxTree tree, IReadOnlyDictionary<InvocationExpressionSyntax, Site> byNode, IReadOnlySet<int> convert)
    {
        var text = tree.GetText();
        var rewriter = new ChainRewriter(byNode, convert, text);
        var root = rewriter.Visit(tree.GetRoot())!;

        // A component that is not the framework's own reaches a host through an entry INJECTED into the
        // host's own partial, so the host has to be `partial` (RASK036). Tag entries need nothing — they
        // are inherited from RaskMarkup — which is why this only runs for the sites that do.
        var needsPartial = byNode.Values
            .Where(s => convert.Contains(s.Id) && s.NeedsInjectedEntry)
            .Select(s => s.Id.ToString())
            .ToHashSet(StringComparer.Ordinal);

        if (needsPartial.Count > 0)
        {
            var hosts = root.DescendantNodes()
                .Where(n => n.GetAnnotations(AnnotationKind).Any(a => needsPartial.Contains(a.Data!)))
                .SelectMany(n => n.Ancestors().OfType<TypeDeclarationSyntax>())
                .Distinct()
                .Where(t => !t.Modifiers.Any(SyntaxKind.PartialKeyword))
                .ToList();

            if (hosts.Count > 0)
            {
                root = root.ReplaceNodes(hosts, (original, current) => MakePartial(current));
            }
        }

        return tree.WithRootAndOptions(root, tree.Options);
    }

    // `partial` has to be the last modifier before the type keyword. When the declaration has no
    // modifiers at all the keyword also owns the line's indentation, so that trivia moves with it.
    private static TypeDeclarationSyntax MakePartial(TypeDeclarationSyntax declaration)
    {
        var partial = Token(SyntaxKind.PartialKeyword).WithTrailingTrivia(Space);
        if (declaration.Modifiers.Count > 0)
        {
            return declaration.WithModifiers(declaration.Modifiers.Add(partial));
        }

        var keyword = declaration.Keyword;
        return declaration
            .WithKeyword(keyword.WithLeadingTrivia())
            .WithModifiers(TokenList(partial.WithLeadingTrivia(keyword.LeadingTrivia)));
    }

    private sealed class ChainRewriter : CSharpSyntaxRewriter
    {
        private readonly IReadOnlyDictionary<InvocationExpressionSyntax, Site> _byNode;
        private readonly IReadOnlySet<int> _convert;
        private readonly Microsoft.CodeAnalysis.Text.SourceText _text;

        public ChainRewriter(
            IReadOnlyDictionary<InvocationExpressionSyntax, Site> byNode,
            IReadOnlySet<int> convert,
            Microsoft.CodeAnalysis.Text.SourceText text)
        {
            _byNode = byNode;
            _convert = convert;
            _text = text;
        }

        public override SyntaxNode? VisitInvocationExpression(InvocationExpressionSyntax node)
        {
            // Children first: an argument may itself be a call site, and its rewritten text is what the
            // chain has to carry.
            var visited = (InvocationExpressionSyntax)base.VisitInvocationExpression(node)!;

            if (!_byNode.TryGetValue(node, out var site) || !_convert.Contains(site.Id))
            {
                return visited;
            }

            var indent = IndentOf(node);
            var parts = new List<string>();
            for (var i = 0; i < visited.ArgumentList.Arguments.Count; i++)
            {
                parts.Add($".{site.Setters[i]}({visited.ArgumentList.Arguments[i].Expression.ToFullString().Trim()})");
            }

            var single = site.ComponentName + string.Concat(parts);
            var multiline = node.ArgumentList.Span.Length > 0
                            && (node.ToString().Contains('\n') || indent.Length + single.Length > 116);

            var code = parts.Count == 0 || !multiline
                ? single
                : site.ComponentName + string.Concat(parts.Select(p => "\n" + indent + "    " + p));

            return ParseExpression(code)
                .WithTriviaFrom(visited)
                .WithAdditionalAnnotations(new SyntaxAnnotation(AnnotationKind, site.Id.ToString()));
        }

        // The whitespace that opens the line the call site starts on — the chain's continuation lines are
        // indented one step past it, which `dotnet format` then normalises.
        private string IndentOf(SyntaxNode node)
        {
            var line = _text.Lines.GetLineFromPosition(node.SpanStart);
            var raw = line.ToString();
            var count = 0;
            while (count < raw.Length && (raw[count] == ' ' || raw[count] == '\t'))
            {
                count++;
            }

            return raw.Substring(0, count);
        }
    }
}
