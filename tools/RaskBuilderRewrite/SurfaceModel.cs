using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Rask.Tools.BuilderRewrite;

/// <summary>
///     The three questions the rewrite has to answer about a call site, all of them semantic: is this a
///     generated factory, is this place allowed to name a builder entry, and what is the setter called.
/// </summary>
internal sealed class SurfaceModel
{
    private readonly CSharpCompilation _compilation;
    private readonly INamedTypeSymbol _component;
    private readonly INamedTypeSymbol _raskMarkup;
    private readonly INamedTypeSymbol? _raskMarkupAttribute;

    // setter name -> the setters that carry it, across this assembly and every referenced one.
    private readonly Dictionary<string, List<IMethodSymbol>> _setters =
        new(StringComparer.Ordinal);

    // factory name -> every generated factory carrying it, for the sites binding cannot reach.
    private readonly Dictionary<string, List<IMethodSymbol>> _factories =
        new(StringComparer.Ordinal);

    // Every builder PIN, indexed by what it is called on: a seed (the entry a generic component hands
    // back) or a stage (a component that takes more than one pin, between the two).
    private readonly Dictionary<string, List<IMethodSymbol>> _pinsByReceiver =
        new(StringComparer.Ordinal);

    private SurfaceModel(
        CSharpCompilation compilation,
        INamedTypeSymbol component,
        INamedTypeSymbol raskMarkup,
        INamedTypeSymbol? raskMarkupAttribute)
    {
        _compilation = compilation;
        _component = component;
        _raskMarkup = raskMarkup;
        _raskMarkupAttribute = raskMarkupAttribute;

        // The setters live in `RaskBuilderSetters{Assembly}` classes in the GLOBAL namespace — one per
        // assembly that declares components — because an extension method is only found when its
        // namespace is in scope and the global namespace encloses all. That naming is the whole index.
        foreach (var type in compilation.GlobalNamespace.GetTypeMembers()
                     .Where(t => t.Name.StartsWith("RaskBuilderSetters", StringComparison.Ordinal)))
        {
            foreach (var method in type.GetMembers().OfType<IMethodSymbol>()
                         .Where(m => m.IsExtensionMethod && m.Parameters.Length >= 1))
            {
                if (!_setters.TryGetValue(method.Name, out var list))
                {
                    _setters[method.Name] = list = new List<IMethodSymbol>();
                }

                list.Add(method);
            }
        }

        // Steps are INSTANCE methods on the seed and on each state, so the receiver is the declaring
        // type. Indexed by receiver rather than by name: a chain is read backwards from the component it
        // has to produce, and the receiver is what links one step to the next.
        foreach (var type in AllNamespaces(compilation.GlobalNamespace)
                     .SelectMany(n => n.GetTypeMembers())
                     .Where(IsChainState))
        {
            foreach (var method in type.GetMembers().OfType<IMethodSymbol>()
                         .Where(m => m is { MethodKind: MethodKind.Ordinary, IsStatic: false }
                                     && m.Parameters.Length == 1
                                     && m.DeclaredAccessibility != Accessibility.Private))
            {
                var receiver = Key(type);
                if (!_pinsByReceiver.TryGetValue(receiver, out var list))
                {
                    _pinsByReceiver[receiver] = list = new List<IMethodSymbol>();
                }

                list.Add(method);
            }
        }

        foreach (var generated in AllNamespaces(compilation.GlobalNamespace)
                     .SelectMany(n => n.GetTypeMembers("Generated"))
                     .Where(t => t.IsStatic))
        {
            foreach (var method in generated.GetMembers().OfType<IMethodSymbol>().Where(IsFactory))
            {
                if (!_factories.TryGetValue(method.Name, out var list))
                {
                    _factories[method.Name] = list = new List<IMethodSymbol>();
                }

                list.Add(method);
            }
        }
    }

    private static IEnumerable<INamespaceSymbol> AllNamespaces(INamespaceSymbol root)
    {
        yield return root;
        foreach (var child in root.GetNamespaceMembers().SelectMany(AllNamespaces))
        {
            yield return child;
        }
    }

    public static SurfaceModel? TryCreate(CSharpCompilation compilation)
    {
        var component = compilation.GetTypeByMetadataName("Rask.Core.Component");
        var markup = compilation.GetTypeByMetadataName("Rask.Core.RaskMarkup");
        return component is null || markup is null
            ? null
            : new SurfaceModel(
                compilation, component, markup,
                compilation.GetTypeByMetadataName("Rask.Core.RaskMarkupAttribute"));
    }

    /// <summary>The assembly that declares <c>RaskMarkup</c> — its components arrive by inheritance.</summary>
    public IAssemblySymbol FrameworkAssembly => _raskMarkup.ContainingAssembly;

    /// <summary>
    ///     The names the 166 framework entries occupy. A type that already has a member with one of these
    ///     names cannot take <c>: RaskMarkup</c> without a <c>new</c> on it (CS0108).
    /// </summary>
    public HashSet<string> FrameworkEntryNames() =>
        _raskMarkup.GetMembers()
            .Where(m => m is IPropertySymbol or IMethodSymbol { MethodKind: MethodKind.Ordinary })
            .Select(m => m.Name)
            .ToHashSet(StringComparer.Ordinal);

    /// <summary>
    ///     The factory a call site MEANT, when the call no longer binds to anything.
    /// </summary>
    /// <remarks>
    ///     Making a type a markup host displaces every factory whose component has a METHOD entry — a
    ///     generic or bound control — because member lookup finds the entry and stops, and a
    ///     <c>using static … Generated</c> import is never consulted after that. The call site is then a
    ///     compile error until it is rewritten, which is exactly the state the rewrite runs in. Binding
    ///     gives nothing there, so the factory is found by name and by the argument names the site used.
    /// </remarks>
    public IMethodSymbol? FindDisplacedFactory(
        string name, IReadOnlyList<string?> argumentNames, IReadOnlyCollection<string>? mustHave = null)
    {
        if (!_factories.TryGetValue(name, out var candidates))
        {
            return null;
        }

        var named = argumentNames.Where(n => n is not null).ToList();
        var applicable = candidates
            .Where(m => m.Parameters.Length >= argumentNames.Count
                        && named.All(n => m.Parameters.Any(p => p.Name == n)))
            .ToList();

        // Narrowest applicable is the wrong answer when a component has BOTH a bound and a controlled
        // factory: they share every display parameter, so a site that names only those is applicable to
        // each, and the controlled one is narrower — but the site passed a bind expression positionally
        // and MEANT the bound one. Without this, every `BsSelect(() => m.Plan, …)` resolved to a factory
        // with no `Bind` parameter at all and was left behind for a pin it did supply.
        //
        // The entry's pins are the disambiguator, since they are exactly what the rewritten site has to
        // pass. Applied as a preference rather than a filter: a component with one factory has nothing
        // to disambiguate and must not be excluded by it.
        if (mustHave is { Count: > 0 })
        {
            var pinned = applicable
                .Where(m => mustHave.All(n => m.Parameters.Any(p => p.Name == n)))
                .ToList();
            if (pinned.Count != 0)
            {
                applicable = pinned;
            }
        }

        // A bound control has three factory overloads, one per validator shape, and they agree on every
        // parameter NAME they share — which is all this is read for. The narrowest applicable one is
        // therefore as good as any, and demanding a unique match instead silently skipped every bound
        // site that named a validator.
        return applicable.OrderBy(m => m.Parameters.Length).FirstOrDefault();
    }

    /// <summary>
    ///     A generated factory is a static method on a static <c>Generated</c> class that hands back a
    ///     component. Nothing else in the repo has that shape.
    /// </summary>
    public bool IsFactory(IMethodSymbol method) =>
        method is { IsStatic: true, ContainingType: { Name: "Generated", IsStatic: true } }
        && method.ReturnType is INamedTypeSymbol returned
        && DerivesFromComponent(returned);

    private bool DerivesFromComponent(INamedTypeSymbol type)
    {
        for (var t = type; t is not null; t = t.BaseType)
        {
            if (SymbolEqualityComparer.Default.Equals(t.OriginalDefinition, _component))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    ///     Entries are <c>protected static</c> members, so they are in scope only inside a markup host:
    ///     a type deriving from <see cref="_raskMarkup" /> (which every <c>Component</c> does), a type
    ///     carrying <c>[RaskMarkup]</c>, or a type nested inside one — simple-name lookup walks out
    ///     through enclosing types.
    /// </summary>
    public bool IsInMarkupHost(INamedTypeSymbol? enclosing)
    {
        for (var t = enclosing; t is not null; t = t.ContainingType)
        {
            for (var b = t; b is not null; b = b.BaseType)
            {
                if (SymbolEqualityComparer.Default.Equals(b, _raskMarkup))
                {
                    return true;
                }
            }

            if (_raskMarkupAttribute is not null && t.GetAttributes().Any(a =>
                    SymbolEqualityComparer.Default.Equals(a.AttributeClass, _raskMarkupAttribute)))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    ///     The setter that stands in for a factory parameter, or null when the parameter has none.
    /// </summary>
    /// <remarks>
    ///     A setter takes its property's name — all of them, including a delegate property. That was not
    ///     always true: while a chain received on the component, a delegate-typed property was invocable
    ///     and beat a same-named extension (CS1593), so its setter dropped a leading <c>On</c> or took a
    ///     <c>Set</c> prefix. The chain receives on <c>Build&lt;TComponent&gt;</c> now, so the collision
    ///     is gone and so are both renamings. The name is still checked against the real surface rather
    ///     than assumed: a parameter nothing accepts makes the whole call site unconvertible.
    ///     <para>
    ///         A STEP counts as well as a setter, and that is what this used to miss. A REQUIRED property
    ///         is not a setter on the finished component — the component does not exist until it has been
    ///         supplied — so `Label(&quot;name&quot;)`, `Button(&quot;button&quot;)` and
    ///         `NavLink(url)` all reported `NoSetter` and were left on the factory, which is most of what
    ///         the tool could not finish. Both are spelled `.Name(value)`, so the rewrite emits the same
    ///         text either way; the difference is that a step must come first, which the reorder pass
    ///         settles.
    ///     </para>
    /// </remarks>
    public string? SetterFor(INamedTypeSymbol componentType, IParameterSymbol parameter) =>
        Applies(componentType, parameter.Name) || IsStep(componentType, parameter.Name)
            ? parameter.Name
            : null;

    // What a step hands back, keyed as a component: the chain is unwrapped BEFORE the original
    // definition is taken, because `Build<Form<TModel>>`'s original definition is `Build<T>`.
    private string ReturnedKey(IMethodSymbol method) =>
        Key(ChainedComponent(method.ReturnType).OriginalDefinition);

    // The component a chain builds, for a `Rask.Core.Build<T>`; otherwise the type unchanged.
    private static ITypeSymbol ChainedComponent(ITypeSymbol type) =>
        type is INamedTypeSymbol { IsGenericType: true, Arity: 1 } named
        && string.Equals(named.ConstructedFrom.ToDisplayString(), "Rask.Core.Build<T>", StringComparison.Ordinal)
            ? named.TypeArguments[0]
            : type;

    private static ITypeSymbol StripNullable(ITypeSymbol type) =>
        type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } n
            ? n.TypeArguments[0]
            : type;

    private static IPropertySymbol? FindProperty(INamedTypeSymbol type, string name)
    {
        for (var t = type; t is not null; t = t.BaseType)
        {
            if (t.GetMembers(name).OfType<IPropertySymbol>().FirstOrDefault() is { } p)
            {
                return p;
            }
        }

        return null;
    }

    private bool Applies(INamedTypeSymbol componentType, string setterName)
    {
        if (!_setters.TryGetValue(setterName, out var candidates))
        {
            return false;
        }

        foreach (var candidate in candidates)
        {
            // The receiver is the CHAIN, `Build<TComponent>` — unwrap it before asking what component it
            // applies to. Without this every lookup below fails and the tool reports `NoSetter` for the
            // whole surface, which is exactly what it did the first time it ran after the receiver moved.
            var receiver = ChainedComponent(candidate.Parameters[0].Type);

            // The 93 props inherited from Element are written once as `T Setter<T>(this T e, …) where
            // T : Element`, so the receiver is a type parameter — check the constraint instead.
            if (receiver is ITypeParameterSymbol tp)
            {
                if (tp.ConstraintTypes.Length == 0
                    || tp.ConstraintTypes.Any(c => _compilation.ClassifyConversion(componentType, c).IsImplicit))
                {
                    return true;
                }

                continue;
            }

            // A generic component's setters are generic methods over the component's own type parameter
            // (`Input<T> Type<T>(this Input<T> __c, …)`), so the receiver here is an OPEN type and no
            // conversion exists from the constructed `Input<string>` to it. Matching the unbound
            // definitions is what those setters need; the closed case still goes through the conversion.
            if (receiver is INamedTypeSymbol named)
            {
                for (var t = componentType; t is not null; t = t.BaseType)
                {
                    if (SymbolEqualityComparer.Default.Equals(t.OriginalDefinition, named.OriginalDefinition))
                    {
                        return true;
                    }
                }
            }

            if (_compilation.ClassifyConversion(componentType, receiver).IsImplicit)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    ///     Whether a generic component can be reached by a NO-ARGUMENT method entry at this position —
    ///     <c>Input&lt;string&gt;()</c> rather than the property an ordinary component gets.
    /// </summary>
    /// <remarks>
    ///     A property cannot be generic, so a generic component's entry is a method, and a method entry
    ///     displaces its own same-named factory inside a markup host. That is why these call sites are
    ///     written fully qualified (<c>Rask.Core.Components.Generated.Input(…)</c>) — the simple name is
    ///     already taken. When the entry has a parameterless overload the site converts cleanly to
    ///     <c>Input&lt;T&gt;()</c> plus the usual setters, and the qualification goes away with it.
    /// </remarks>
    /// <remarks>
    ///     The seed's <c>Of&lt;T&gt;()</c> step, not the old generic METHOD entry (<c>Input&lt;T&gt;()</c>)
    ///     the seed replaced — looked up through the entry PROPERTY, because that is where a seed lives.
    ///     <para>
    ///         KNOWN NOT TO FIRE. The generator does emit the step (<c>Input.Of&lt;string&gt;()</c> compiles
    ///         in BsCheck and BuilderBoundControlTests), but this returns false at every site tried, so the
    ///         pin-less generic factory calls in Rask.Core.Tests are still reported as
    ///         <c>GenericFactory</c>. Unresolved: whether <see cref="SemanticModel.LookupSymbols" /> is not
    ///         returning the inherited <c>protected static</c> entry at that position, or the seed-type
    ///         match is wrong. Do not assume this path works.
    ///     </para>
    /// </remarks>
    public static bool HasParameterlessEntry(SemanticModel model, int position, string name) =>
        model.LookupSymbols(position, name: name)
            .OfType<IPropertySymbol>()
            .Select(p => p.Type)
            .Any(seed => IsSeed(seed)
                         && seed.GetMembers("Of").OfType<IMethodSymbol>()
                             .Any(m => m.Parameters.Length == 0 && m.TypeParameters.Length > 0));

    /// <summary>
    ///     Whether a bound control's one-argument entry — <c>Input&lt;T&gt;(Expression&lt;Func&lt;T&gt;&gt;
    ///     Bind)</c> — is reachable here. That argument is what infers <c>T</c>, so a site already passing
    ///     one keeps it and turns everything else into setters.
    /// </summary>
    public static bool HasBindEntry(SemanticModel model, int position, string name) =>
        model.LookupSymbols(position, name: name)
            .OfType<IMethodSymbol>()
            .Any(m => m is { Parameters.Length: 1, IsStatic: true } && m.Parameters[0].Name == "Bind");

    /// <summary>One step of a pin chain: the method to call and the property its argument sets.</summary>
    internal sealed record PinStep(string Name, IParameterSymbol Parameter);

    /// <summary>
    ///     How to build <paramref name="component" /> from its seed — <c>Bind(x)</c>, or
    ///     <c>Bind(x).Options(y)</c> when one call cannot pin every type argument. Empty when the
    ///     component has no seed entry.
    /// </summary>
    /// <remarks>
    ///     Read BACKWARDS, from the component the site has to produce to the seed the chain starts at,
    ///     because that is the direction the index actually answers: a pin knows what it returns, and only
    ///     the last step returns the component. Reading forwards would mean guessing which of a seed's
    ///     several pins (<c>Bind</c>, <c>Options</c>, <c>Value</c>) this site meant.
    /// </remarks>
    public IReadOnlyList<IReadOnlyList<PinStep>> PinChainsFor(INamedTypeSymbol component)
    {
        var wanted = Key(component.OriginalDefinition);
        var chains = new List<IReadOnlyList<PinStep>>();

        // ReturnedKey, not `Key(m.ReturnType.OriginalDefinition)`: a step hands back the CHAIN over what
        // it built, and the original definition of `Build<Form<TModel>>` is `Build<T>` — which matches no
        // component, so every pin chain came back empty and every generic factory site was left behind.
        foreach (var last in _pinsByReceiver.Values.SelectMany(p => p)
                     .Where(m => ReturnedKey(m) == wanted))
        {
            var receiver = (ITypeSymbol)last.ContainingType;
            var step = new PinStep(last.Name, last.Parameters[0]);

            // A seed receiver is the whole chain; a STAGE receiver means one pin came before it, and the
            // step that produced the stage is the one that starts at the seed.
            if (IsSeed(receiver))
            {
                chains.Add([step]);
                continue;
            }

            foreach (var first in _pinsByReceiver.Values.SelectMany(p => p)
                         .Where(m => ReturnedKey(m) == Key(receiver.OriginalDefinition)
                                     && IsSeed(m.ContainingType)))
            {
                chains.Add([new PinStep(first.Name, first.Parameters[0]), step]);
            }
        }

        // Longest first: a staged chain says more about the site than a single pin, and a caller that
        // wants the short one asks for it by name.
        return chains.OrderByDescending(c => c.Count).ToList();
    }

    /// <summary>
    ///     The component a seed builds. A seed serves one component NAME, so the last step of any chain
    ///     starting there names it; where two arities once shared a seed there is now only one.
    /// </summary>
    public INamedTypeSymbol? ComponentForSeed(ITypeSymbol seed)
    {
        if (!_pinsByReceiver.TryGetValue(Key(seed), out var pins))
        {
            return null;
        }

        foreach (var pin in pins)
        {
            var returned = pin.ReturnType.OriginalDefinition;

            // A pin that hands back a STAGE has not built anything yet; follow it one step on.
            if (returned.Name.StartsWith("RaskStage_", StringComparison.Ordinal))
            {
                if (_pinsByReceiver.TryGetValue(Key(returned), out var next)
                    && next.FirstOrDefault()?.ReturnType.OriginalDefinition is INamedTypeSymbol staged)
                {
                    return staged;
                }

                continue;
            }

            if (returned is INamedTypeSymbol direct)
            {
                return direct;
            }
        }

        return null;
    }

    /// <summary>
    ///     The method names that are STEPS for the chain rooted at <paramref name="seedName" /> — the
    ///     pins and required properties, which have to come before anything else.
    /// </summary>
    public HashSet<string> OpeningNamesFor(SemanticModel model, int position, string seedName)
    {
        var seed = model.LookupSymbols(position, name: seedName)
            .OfType<IPropertySymbol>()
            .FirstOrDefault(p => IsSeed(p.Type));
        return seed is not null && _pinsByReceiver.TryGetValue(Key(seed.Type), out var pins)
            ? new HashSet<string>(pins.Select(p => p.Name), StringComparer.Ordinal)
            : new HashSet<string>(StringComparer.Ordinal);
    }

    public HashSet<string> StepNamesFor(SemanticModel model, int position, string seedName)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        var seed = model.LookupSymbols(position, name: seedName)
            .OfType<IPropertySymbol>()
            .FirstOrDefault(p => IsSeed(p.Type));
        if (seed is null)
        {
            return names;
        }

        // Walk the machine: every method reachable from the seed, and from each state it leads to, is a
        // step. Everything else on the finished component is a setter and stays where it was written.
        var queue = new Queue<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        queue.Enqueue(Key(seed.Type));

        while (queue.Count != 0)
        {
            var receiver = queue.Dequeue();
            if (!seen.Add(receiver) || !_pinsByReceiver.TryGetValue(receiver, out var pins))
            {
                continue;
            }

            foreach (var pin in pins)
            {
                names.Add(pin.Name);
                var returned = pin.ReturnType.OriginalDefinition;
                if (returned.Name.StartsWith("RaskPending_", StringComparison.Ordinal)
                    || returned.Name.StartsWith("RaskStage_", StringComparison.Ordinal))
                {
                    queue.Enqueue(Key(returned));
                }
            }
        }

        return names;
    }

    private static bool IsSeed(ITypeSymbol type) =>
        type.Name.StartsWith("RaskSeed_", StringComparison.Ordinal);

    /// <summary>
    ///     Whether <paramref name="name" /> is a STEP of <paramref name="component" />'s chain rather than
    ///     a setter on the finished component. Both are spelled `.Name(value)`, so a rewrite emits the
    ///     same text either way — what differs is that a step has to come first, which the reorder pass
    ///     settles.
    /// </summary>
    public bool IsStep(INamedTypeSymbol component, string name)
    {
        var wanted = Key(component.OriginalDefinition);
        foreach (var pins in _pinsByReceiver.Values)
        {
            foreach (var pin in pins.Where(m => string.Equals(m.Name, name, StringComparison.Ordinal)))
            {
                // A step either produces the component or moves to a state that eventually does.
                for (var returned = ChainedComponent(pin.ReturnType).OriginalDefinition; returned is not null;)
                {
                    if (Key(returned) == wanted)
                    {
                        return true;
                    }

                    if (returned is not INamedTypeSymbol named || !IsChainState(named)
                        || !_pinsByReceiver.TryGetValue(Key(named), out var next)
                        || next.Count == 0)
                    {
                        break;
                    }

                    returned = next[0].ReturnType.OriginalDefinition;
                }
            }
        }

        return false;
    }

    // The generated chain types: where a chain starts, and every state it passes through.
    private static bool IsChainState(INamedTypeSymbol type) =>
        type.Name.StartsWith("RaskSeed_", StringComparison.Ordinal)
        || type.Name.StartsWith("RaskStage_", StringComparison.Ordinal)
        || type.Name.StartsWith("RaskPending_", StringComparison.Ordinal);

    private static string Key(ITypeSymbol type) =>
        type.OriginalDefinition.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

    /// <summary>
    ///     The parameters of the reachable entry that <b>pins</b> — the one with arguments, as opposed to
    ///     the parameterless overload. Empty when there is no such entry.
    /// </summary>
    /// <remarks>
    ///     A generic entry takes one parameter per type parameter it has to pin, because C# infers a
    ///     method's type arguments all or nothing: <c>Input(() =&gt; m.Name)</c> pins one,
    ///     <c>BsSelect(() =&gt; m.PersonId, people)</c> pins two. Those arguments stay arguments at the
    ///     rewritten site — everything else becomes a setter — so a rewrite has to know which they are and
    ///     in what order, rather than assuming the single <c>Bind</c> the bound controls happen to have.
    ///     The overload is picked by ARITY, since same-named components of different arity coexist as
    ///     overloads and only the one matching this call site's component is the right shape to read.
    /// </remarks>
    public IReadOnlyList<IParameterSymbol> EntryPinParameters(
        SemanticModel model, int position, string name, int arity) =>
        model.LookupSymbols(position, name: name)
            .OfType<IMethodSymbol>()
            // Not the FACTORY, which is also static, also in scope (`using static … Generated`), also
            // named after the component and also generic. Taking it for the entry made every one of its
            // parameters look like a pin, so a site was left behind for not supplying a `PageSize` that
            // is an ordinary setter — and the entry it was compared against did not exist at all.
            .Where(m => m.IsStatic && m.Parameters.Length > 0 && m.TypeParameters.Length == arity
                        && !IsFactory(m))
            // Fewest parameters, because a component may also have FORWARDERS of the same name and arity
            // that fold several props into one call (`BsDataGrid`'s). Those are not the entry, and taking
            // one for the entry demands pins the entry never had — every BsDataGrid site was then left
            // behind for not supplying a `PageSize` that is an ordinary setter.
            .OrderBy(m => m.Parameters.Length)
            .FirstOrDefault()
            ?.Parameters.ToList()
        ?? (IReadOnlyList<IParameterSymbol>)[];

    /// <summary>
    ///     Whether the type declares a nested component — which makes it unable to become a markup host.
    /// </summary>
    /// <remarks>
    ///     Consumer entries are injected into the host's own <c>partial</c>, one member per reachable
    ///     component, named after the component. A component nested inside the would-be host is therefore
    ///     both a type it declares and a member it is about to be given: CS0102, in generated source.
    /// </remarks>
    public bool DeclaresNestedComponent(INamedTypeSymbol type) =>
        type.GetTypeMembers().Any(nested => DerivesFromComponent(nested) || DeclaresNestedComponent(nested));

    /// <summary>The enclosing type of a node, or null when it has none.</summary>
    public static INamedTypeSymbol? EnclosingType(SemanticModel model, SyntaxNode node)
    {
        var declaration = node.Ancestors().OfType<BaseTypeDeclarationSyntax>().FirstOrDefault();
        return declaration is null ? null : model.GetDeclaredSymbol(declaration);
    }
}
