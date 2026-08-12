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
    MixedSurface,
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
    // Where the entry's own parameters were written at the site, in the ENTRY's order — the arguments
    // that stay arguments. One for a bound control's `Bind`, more for a component whose entry has to pin
    // more than one type parameter, none for a plain or controlled site.
    IReadOnlyList<int> PinArguments,
    // The pin methods to call on the seed, one per PinArguments entry — `Bind`, or `Bind` then
    // `Options` when one call cannot pin every type argument.
    IReadOnlyList<string> PinNames,
    // Which of Setters are chain STEPS rather than setters. Spelled identically, but a step has to come
    // first — and the rewrite has to emit them that way itself, because the verification that follows
    // compiles what it wrote.
    IReadOnlyList<string> StepCalls,
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
                // Compared as ORIGINAL DEFINITIONS: a pin chain infers its type arguments, so the
                // rewritten site produces `BsSelect<string, string>` where the component it was resolved
                // against is the unbound `BsSelect<TValue, TItem>`. Comparing constructed types rejected
                // every correctly-rewritten generic site as if its name had been hijacked.
                // A STAGE is a legitimate result here: a site that supplied one pin and set the rest
                // further down the chain rewrites to `BsSelect.Bind(x)`, and the annotated node stops
                // there — the `.Options(y)` that finishes it was already the next call in the source.
                // Unwrapped BEFORE OriginalDefinition, not after: a chain produces `Build<TComponent>`,
                // and the original definition of that is `Build<T>` — whose type argument is a type
                // parameter, not the component. Taking them in the wrong order makes every correctly
                // rewritten site look hijacked, which is what it did the first time it ran after the
                // chain's receiver moved off the component.
                var produced = ChainedComponent(model2.GetTypeInfo(node).Type)?.OriginalDefinition;
                // A STAGE or a PENDING state is a legitimate result here: a site that supplied one step
                // and set the rest further down the chain rewrites to `BsMultiSelect.Bind(x)`, and the
                // annotated node stops there — the `.Options(y)` that finishes it was already the next
                // call in the source.
                var staged = produced is not null
                             && (produced.Name.StartsWith("RaskStage_", StringComparison.Ordinal)
                                 || produced.Name.StartsWith("RaskPending_", StringComparison.Ordinal));
                if (produced is null || (!staged && Fqn(produced) != site.ComponentFullName))
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

    // The component a chain builds, for a `Rask.Core.Build<T>`; otherwise the type unchanged.
    private static ITypeSymbol? ChainedComponent(ITypeSymbol? type) =>
        type is INamedTypeSymbol { IsGenericType: true, Arity: 1 } named
        && string.Equals(named.ConstructedFrom.ToDisplayString(), "Rask.Core.Build<T>", StringComparison.Ordinal)
            ? named.TypeArguments[0]
            : type;

    private static HashSet<string> ErrorIds(IEnumerable<Diagnostic> diagnostics) =>
        diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Select(Key).ToHashSet(StringComparer.Ordinal);

    private static string Key(Diagnostic d) => d.Id + "|" + d.GetMessage();

    private static string Fqn(ITypeSymbol type) =>
        type.OriginalDefinition.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

    // ---- collection ------------------------------------------------------------------------------

    /// <summary>
    ///     The components this file ALREADY builds through a builder entry.
    /// </summary>
    /// <remarks>
    ///     A file that builds the same component both ways is almost always a comparison: two spellings of
    ///     one tree, with an assertion that they agree. Converting the factory half leaves two identical
    ///     halves and a test that passes while proving nothing — which is exactly what happened to this
    ///     tool's own parity test, and was caught by reading the diff rather than by anything failing.
    ///     <para>
    ///     So the refusal is per COMPONENT, not per file: a file may hold `Div` chains and a leftover
    ///     `Form(…)` factory without either being a comparison, and that still converts. Only a component
    ///     spelled both ways in one file is held back — and reported, so the choice is a person's.
    ///     </para>
    /// </remarks>
    private HashSet<string> EntryBuiltComponents(SyntaxTree tree, SemanticModel model)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var name in tree.GetRoot().DescendantNodes().OfType<SimpleNameSyntax>())
        {
            var text = name.Identifier.ValueText;

            // An entry is a member named after its own component type — that is the whole shape, and it
            // is what separates `Div` the entry from `Div` the type or `Div` the factory (excluded
            // explicitly, since a factory is also a static member returning its own name).
            switch (model.GetSymbolInfo(name).Symbol)
            {
                case IPropertySymbol { IsStatic: true } p when p.Name == text && p.Type.Name == text:
                case IMethodSymbol { IsStatic: true } m when m.Name == text
                                                             && m.ReturnType.Name == text
                                                             && !_surface.IsFactory(m):
                    names.Add(text);
                    break;
            }
        }

        return names;
    }

    private List<Site> Collect(SyntaxTree tree, SemanticModel model)
    {
        var sites = new List<Site>();
        var entryBuilt = EntryBuiltComponents(tree, model);
        var id = 0;

        foreach (var node in tree.GetRoot().DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            // A site the PREVIOUS shape already moved: `BsSelect(() => m.Plan)`, written when a generic
            // entry was a method. The entry is a seed property now, so this is not a factory call and not
            // an invocation of anything — it is `X(args)` where X is not invocable. Its arguments are
            // already the pins, in order, so it re-reads as the chain without consulting a factory at all.
            if (Resolve(model, node) is not { } method)
            {
                var seedArgs = node.ArgumentList.Arguments;
                if (SeedInvocation(model, node) is not ({ } seedComponent, { Count: > 0 } seedChain)
                    || seedArgs.Count == 0 || seedArgs.Count > seedChain.Count
                    || seedArgs.Any(a => a.NameColon is not null))
                {
                    continue;
                }

                // Only the leading pins move into the receiver. A site that passed one argument and then
                // set the rest — `BsSelect(() => m.Plan).Options(plans)` — already spells the second pin
                // as the next call in the chain, because a pin and a setter for the same property are
                // named alike. It becomes `BsSelect.Bind(() => m.Plan).Options(plans)` untouched.
                seedChain = seedChain.Take(seedArgs.Count).ToList();

                var seedId = id++;
                sites.Add(new Site(
                    seedId, node, seedComponent.Name, Fqn(seedComponent.OriginalDefinition),
                    seedComponent.Name,
                    tree.FilePath, tree.GetLineSpan(node.Span).StartLinePosition.Line + 1,
                    SiteVerdict.Convertible, null,
                    Enumerable.Repeat(string.Empty, seedChain.Count).ToList(),
                    Enumerable.Range(0, seedChain.Count).ToList(),
                    seedChain.Select(s => s.Name).ToList(),
                    Array.Empty<string>(),
                    !SymbolEqualityComparer.Default.Equals(
                        seedComponent.ContainingAssembly, _surface.FrameworkAssembly)));
                continue;
            }

            var component = (INamedTypeSymbol)method.ReturnType;
            var siteId = id++;
            var name = component.Name;
            var line = tree.GetLineSpan(node.Span).StartLinePosition.Line + 1;
            var file = tree.FilePath;

            var typeArguments = "";
            var pinArguments = new List<int>();
            var pinNames = new List<string>();
            var stepCalls = new List<string>();

            Site Make(SiteVerdict verdict, string? detail, IReadOnlyList<string>? setters = null) =>
                new(siteId, node, name, Fqn(component.OriginalDefinition), name + typeArguments, file, line,
                    verdict, detail,
                    setters ?? Array.Empty<string>(),
                    pinArguments,
                    pinNames,
                    stepCalls,
                    !SymbolEqualityComparer.Default.Equals(
                        component.ContainingAssembly, _surface.FrameworkAssembly));

            if (!_surface.IsInMarkupHost(SurfaceModel.EnclosingType(model, node)))
            {
                sites.Add(Make(SiteVerdict.NotInMarkupHost, null));
                continue;
            }

            // This file already builds this very component through an entry. Converting would collapse
            // the two spellings into one, and if they were there to be compared the comparison goes with
            // them — silently, because the test still passes.
            if (entryBuilt.Contains(name))
            {
                sites.Add(Make(SiteVerdict.MixedSurface, "already built by an entry in this file"));
                continue;
            }

            // A generic component's entry is a METHOD, because a property cannot be generic — and a
            // method entry displaces its own same-named factory inside a markup host, which is why these
            // sites are written fully qualified in the first place. A parameterless overload of the entry
            // takes the site as it stands; anything else (a forwarder that folds an argument the entry
            // does not have) moves with the entry, not before it.
            if (method.IsGenericMethod || component.IsGenericType)
            {
                // The entry's own parameters are what infer the type arguments, so a site that passes all
                // of them keeps them in place and turns the rest into setters. Reading
                // `Input(() => m.Name).Class("x")` rather than `Input<string>().Bind(…)`, and working
                // where the type argument was inferred rather than written.
                //
                // ALL of them or none: C# infers a method's type arguments all or nothing, so a site that
                // supplies only some of the pins would leave the rest uninferable and cannot be rewritten
                // this way — it waits, rather than being converted into something that does not compile.
                // Which way in did this site actually use? A component publishes several — Bind for a
                // bound site, Value or Options for a controlled one — and only the arguments the site
                // WROTE can say which. Choosing by what the factory merely declares picked `Value` for
                // every `Input(() => m.Name)` and left them all behind.
                // Which way in did this site actually use? A component publishes several — Bind for a
                // bound site, Value or Options for a controlled one — and only the arguments the site
                // WROTE can say which. Choosing by what a factory merely declares picked `Value` for
                // every `Input(() => m.Name)` and left them all behind.
                //
                // The factory is re-resolved per chain, because a displaced site was found by NAME and a
                // component with both a bound and a controlled factory has two that fit: the controlled
                // one is narrower and wins, and it has no `Bind` parameter at all, so the bound chain
                // could never be matched against it.
                var pins = new List<IParameterSymbol>();
                var argumentNames = node.ArgumentList.Arguments
                    .Select(a => a.NameColon?.Name.Identifier.ValueText).ToList();

                foreach (var candidate in _surface.PinChainsFor(component))
                {
                    var against = method;
                    if (candidate.Any(s => against.Parameters.All(p => p.Name != s.Parameter.Name)))
                    {
                        against = _surface.FindDisplacedFactory(
                                      name, argumentNames,
                                      candidate.Select(s => s.Parameter.Name).ToList())
                                  ?? against;
                    }

                    var indexes = new List<int>();
                    foreach (var step in candidate)
                    {
                        var parameter = against.Parameters.FirstOrDefault(p => p.Name == step.Parameter.Name);
                        var at = parameter is null ? -1 : ArgumentIndexFor(node, against, parameter);
                        if (at < 0)
                        {
                            indexes.Clear();
                            break;
                        }

                        indexes.Add(at);
                    }

                    if (indexes.Count == candidate.Count)
                    {
                        method = against;
                        pins = candidate.Select(s => s.Parameter).ToList();
                        pinNames = candidate.Select(s => s.Name).ToList();
                        pinArguments.AddRange(indexes);
                        break;
                    }
                }

                string? unsupplied = pins.Count == 0 ? "any" : null;

                if (pinArguments.Count == 0 && !SurfaceModel.HasParameterlessEntry(model, node.SpanStart, name))
                {
                    sites.Add(Make(SiteVerdict.GenericFactory,
                        pins.Count == 0 ? "no entry that pins" : $"site does not supply pin '{unsupplied}'"));
                    continue;
                }

                if (pinArguments.Count == 0)
                {
                    // Prefer what the call site wrote. A factory found by NAME rather than by binding —
                    // which is how a displaced site is found — is the unbound definition, so its type
                    // arguments are still `T`; only the syntax knows the site meant `<string>`. When the
                    // site inferred them and binding could not supply them either, there is nothing to
                    // write and the site waits.
                    var written = (node.Expression as GenericNameSyntax)?.TypeArgumentList.ToString();
                    if (written is null && component.TypeArguments.Any(t => t.TypeKind == TypeKind.TypeParameter))
                    {
                        sites.Add(Make(SiteVerdict.GenericFactory,
                            unsupplied is null
                                ? "type argument is neither written nor bound"
                                : $"type argument is neither written nor bound; pin '{unsupplied}' not supplied"));
                        continue;
                    }

                    // `.Of<T>()`, the seed's explicit-type opening — not the old `Input<T>()` generic
                    // METHOD entry, which the seed replaced. A generic component used non-generically
                    // (`Input<string>()`, no bind and no value) has nothing to infer from, and this is the
                    // step that settles the type without pinning a property.
                    typeArguments = ".Of" + (written ?? "<" + string.Join(", ", component.TypeArguments.Select(t =>
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

                // A pin stays where it is — it is the entry's own parameter, not a setter.
                if (pinArguments.Contains(i))
                {
                    setters.Add("");
                    continue;
                }

                // A required property has no setter any more — it is a chain STEP. The call is spelled
                // the same, so the rewrite is unchanged; only its position matters, and the reorder pass
                // is what puts it there.
                var setter = _surface.SetterFor(component, parameter);
                if (setter is null && _surface.IsStep(component, parameter.Name))
                {
                    setter = parameter.Name;
                    stepCalls.Add(setter);
                }
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

    // `X(args)` where X is a SEED property rather than anything invocable — the shape the previous
    // generic-entry spelling leaves behind. Answers which component it meant and the pin chain that
    // builds it, or nulls when this is not that.
    private (INamedTypeSymbol?, IReadOnlyList<SurfaceModel.PinStep>) SeedInvocation(
        SemanticModel model, InvocationExpressionSyntax node)
    {
        if (node.Expression is not SimpleNameSyntax simple)
        {
            return (null, []);
        }

        var seed = model.LookupSymbols(node.SpanStart, name: simple.Identifier.ValueText)
            .OfType<IPropertySymbol>()
            .FirstOrDefault(p => p.Type.Name.StartsWith("RaskSeed_", StringComparison.Ordinal));
        if (seed is null)
        {
            return (null, []);
        }

        var component = _surface.ComponentForSeed(seed.Type);
        if (component is null)
        {
            return (null, []);
        }

        // The chain this spelling meant is the one rooted at the pin the old method entry took, which was
        // always the inference property — `Bind` for a form control. Preferred rather than assumed: a
        // component with no Bind falls back to its longest chain.
        var chains = _surface.PinChainsFor(component);
        return (component, chains.FirstOrDefault(ch => ch[0].Name == "Bind") ?? chains.FirstOrDefault() ?? []);
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

        // A name that now binds to a SEED is not a displaced factory, however much the name matches one:
        // it is the previous generic-entry spelling, whose arguments are pins rather than factory
        // parameters — and whose later pins may already be written as setters further down the chain.
        // Resolving it as a factory demands every pin in the argument list and rejects the site.
        if (model.LookupSymbols(node.SpanStart, name: simple.Identifier.ValueText)
            .OfType<IPropertySymbol>()
            .Any(p => p.Type.Name.StartsWith("RaskSeed_", StringComparison.Ordinal)))
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
            var steps = new List<string>();
            var parts = new List<string>();
            var receiver = site.Receiver;
            for (var i = 0; i < visited.ArgumentList.Arguments.Count; i++)
            {
                var text = visited.ArgumentList.Arguments[i].Expression.ToFullString().Trim();
                if (site.PinArguments.Contains(i))
                {
                    continue;
                }

                var call = $".{site.Setters[i]}({text})";
                (site.StepCalls.Contains(site.Setters[i]) ? steps : parts).Add(call);
            }

            parts.InsertRange(0, steps);

            // The pins are written in the CHAIN's order, which is not necessarily the order the factory
            // call site wrote them in — a site is free to name its arguments in any order, and one that
            // did would otherwise have its pins silently transposed.
            if (site.PinArguments.Count != 0)
            {
                receiver = site.ComponentName;
                for (var i = 0; i < site.PinArguments.Count; i++)
                {
                    var text = visited.ArgumentList.Arguments[site.PinArguments[i]]
                        .Expression.ToFullString().Trim();
                    receiver += $".{site.PinNames[i]}({text})";
                }
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
