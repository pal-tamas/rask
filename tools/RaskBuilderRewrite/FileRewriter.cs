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
    string ComponentFullName,
    string Receiver,
    string File,
    int Line,
    SiteVerdict Verdict,
    string? Detail,
    IReadOnlyList<string> Setters,
    int BindArgument,
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

    /// <summary>
    ///     A file that says this keeps its factory calls. For the tests that hold the two surfaces side by
    ///     side and assert they agree — converting those would delete the comparison and leave a test that
    ///     compares a chain to itself, still green, proving nothing.
    /// </summary>
    public const string OptOutMarker = "rask-rewrite: keep the factory";

    public FileResult Rewrite(SyntaxTree tree)
    {
        if (tree.GetText().ToString().Contains(OptOutMarker, StringComparison.Ordinal))
        {
            return new FileResult(null, Array.Empty<Site>(), Array.Empty<int>(), null);
        }

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
            var hijacked = new HashSet<int>();
            var unattributable = new List<Diagnostic>();

            // "It compiles" is not the test. `F.Head()` rewritten to `Head` compiles perfectly inside a
            // component — and binds to `Component.Head`, the virtual `Component?` a page overrides to add
            // metadata, not to the <head> tag entry it hides. Nothing in the diagnostics says so; the
            // only thing that does is the TYPE of what the chain produced. So every rewritten site is
            // asked what it built, and a site that did not build its own component is reverted whether or
            // not the compiler complained.
            var model2 = candidate.GetSemanticModel(rewritten);
            foreach (var node in root.DescendantNodes().Where(n => n.HasAnnotations(AnnotationKind)))
            {
                var annotation = node.GetAnnotations(AnnotationKind).First();
                if (!int.TryParse(annotation.Data, out var siteId) || !byNode.Values.Any(s => s.Id == siteId))
                {
                    continue;
                }

                var site = byNode.Values.First(s => s.Id == siteId);
                // By NAME, not by symbol identity: `candidate` is a different CSharpCompilation, and a
                // source symbol from one is never reference-equal to the same source symbol in another.
                var produced = model2.GetTypeInfo(node).Type;
                if (produced is null || Fqn(produced) != site.ComponentFullName)
                {
                    rejected.Add(siteId);
                    hijacked.Add(siteId);
                }
            }

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
                ? s with
                {
                    Verdict = SiteVerdict.CompilerRejected,
                    Detail = hijacked.Contains(s.Id)
                        ? "the entry name binds to something else here"
                        : "compiler rejected the chain",
                }
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

    private static string Fqn(ITypeSymbol type) =>
        type.OriginalDefinition.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

    // ---- collection ------------------------------------------------------------------------------

    private List<Site> Collect(SyntaxTree tree, SemanticModel model)
    {
        var sites = new List<Site>();
        var id = 0;

        foreach (var node in tree.GetRoot().DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            var method = Resolve(model, node);
            if (method is null)
            {
                continue;
            }

            var component = (INamedTypeSymbol)method.ReturnType;
            var siteId = id++;
            var name = component.Name;
            var line = tree.GetLineSpan(node.Span).StartLinePosition.Line + 1;
            var file = tree.FilePath;

            var typeArguments = "";
            var bindArgument = -1;

            Site Make(SiteVerdict verdict, string? detail, IReadOnlyList<string>? setters = null) =>
                new(siteId, node, name, Fqn(component), name + typeArguments, file, line,
                    verdict, detail,
                    setters ?? Array.Empty<string>(),
                    bindArgument,
                    !SymbolEqualityComparer.Default.Equals(
                        component.ContainingAssembly, _surface.FrameworkAssembly));

            // `Form<TModel>` is pending its own work and every one of its call sites will move again
            // when it lands, so it is out of this migration entirely.
            if (name is "Form")
            {
                sites.Add(Make(SiteVerdict.FormExcluded, null));
                continue;
            }

            if (!_surface.IsInMarkupHost(SurfaceModel.EnclosingType(model, node)))
            {
                sites.Add(Make(SiteVerdict.NotInMarkupHost, null));
                continue;
            }

            // A generic component's entry is a METHOD, because a property cannot be generic — and a
            // method entry displaces its own same-named factory inside a markup host, which is why these
            // sites are written fully qualified in the first place. A parameterless overload of the entry
            // takes the site as it stands; anything else (a forwarder that folds an argument the entry
            // does not have) moves with the entry, not before it.
            if (method.IsGenericMethod || component.IsGenericType)
            {
                // A bound control's entry takes exactly one argument, `Bind`, and that argument is what
                // infers T — so a site that passes one keeps it in place and turns the rest into setters.
                // Reading `Input(() => m.Name).Class("x")` rather than `Input<string>().Bind(…)`, and
                // working where the type argument was inferred rather than written.
                var bind = method.Parameters.FirstOrDefault(p => p.Name == "Bind");
                if (bind is not null
                    && SurfaceModel.HasBindEntry(model, node.SpanStart, name)
                    && ArgumentIndexFor(node, method, bind) is >= 0 and var index)
                {
                    bindArgument = index;
                }
                else if (!SurfaceModel.HasParameterlessEntry(model, node.SpanStart, name))
                {
                    sites.Add(Make(SiteVerdict.GenericFactory, "no parameterless entry"));
                    continue;
                }

                if (bindArgument < 0)
                {
                    // Prefer what the call site wrote. A factory found by NAME rather than by binding —
                    // which is how a displaced site is found — is the unbound definition, so its type
                    // arguments are still `T`; only the syntax knows the site meant `<string>`. When the
                    // site inferred them and binding could not supply them either, there is nothing to
                    // write and the site waits.
                    var written = (node.Expression as GenericNameSyntax)?.TypeArgumentList.ToString();
                    if (written is null && component.TypeArguments.Any(t => t.TypeKind == TypeKind.TypeParameter))
                    {
                        sites.Add(Make(SiteVerdict.GenericFactory, "type argument is neither written nor bound"));
                        continue;
                    }

                    typeArguments = (written ?? "<" + string.Join(", ", component.TypeArguments.Select(t =>
                        t.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat))) + ">") + "()";
                }
            }

            var setters = new List<string>();
            string? missing = null;
            for (var i = 0; i < node.ArgumentList.Arguments.Count; i++)
            {
                var argument = node.ArgumentList.Arguments[i];
                var parameter = ParameterFor(method, node, argument);
                if (parameter is null)
                {
                    missing = "?";
                    break;
                }

                // The Bind argument stays where it is — it is the entry's own parameter, not a setter.
                if (i == bindArgument)
                {
                    setters.Add("");
                    continue;
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

    // The factory this invocation names — by binding when it still binds, and by name when it does not.
    // It stops binding the moment its type becomes a markup host: the entry displaces it, which is the
    // very state the host pass leaves behind for this one to repair.
    private IMethodSymbol? Resolve(SemanticModel model, InvocationExpressionSyntax node)
    {
        var info = model.GetSymbolInfo(node);
        if (info.Symbol is IMethodSymbol bound)
        {
            return _surface.IsFactory(bound) ? bound : null;
        }

        if (node.Expression is not SimpleNameSyntax simple || info.CandidateSymbols.Length == 0)
        {
            return null;
        }

        // Only when the thing that WON the lookup is this component's own entry — otherwise the call is
        // broken for some reason of its own and is none of this tool's business.
        if (!info.CandidateSymbols.All(c => c.Name == simple.Identifier.ValueText && c.IsStatic))
        {
            return null;
        }

        return _surface.FindDisplacedFactory(
            simple.Identifier.ValueText,
            node.ArgumentList.Arguments.Select(a => a.NameColon?.Name.Identifier.ValueText).ToList());
    }

    // Where in the argument list a given parameter's value was written, or -1 when it was omitted.
    private static int ArgumentIndexFor(
        InvocationExpressionSyntax node, IMethodSymbol method, IParameterSymbol parameter)
    {
        for (var i = 0; i < node.ArgumentList.Arguments.Count; i++)
        {
            if (ReferenceEquals(ParameterFor(method, node, node.ArgumentList.Arguments[i]), parameter))
            {
                return i;
            }
        }

        return -1;
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
            var receiver = site.Receiver;
            for (var i = 0; i < visited.ArgumentList.Arguments.Count; i++)
            {
                var text = visited.ArgumentList.Arguments[i].Expression.ToFullString().Trim();
                if (i == site.BindArgument)
                {
                    receiver = $"{site.ComponentName}({text})";
                    continue;
                }

                parts.Add($".{site.Setters[i]}({text})");
            }

            var single = receiver + string.Concat(parts);
            var multiline = node.ArgumentList.Span.Length > 0
                            && (node.ToString().Contains('\n') || indent.Length + single.Length > 116);

            var code = parts.Count == 0 || !multiline
                ? single
                : receiver + string.Concat(parts.Select(p => "\n" + indent + "    " + p));

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
