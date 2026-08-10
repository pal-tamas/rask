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
    public IMethodSymbol? FindDisplacedFactory(string name, IReadOnlyList<string?> argumentNames)
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
    ///     The name rule is the generator's (<c>ComponentFactoryGenerator.SetterName</c>): a setter takes
    ///     its property's name, except for a RAW delegate property whose name starts with <c>On</c> —
    ///     that property is invocable and would beat a same-named extension (CS1593), so its setter drops
    ///     the prefix. Rather than trust the rule, both candidate names are looked up in the real setter
    ///     index and the one that actually applies to this component wins; a parameter no setter accepts
    ///     makes the whole call site unconvertible.
    /// </remarks>
    public string? SetterFor(INamedTypeSymbol componentType, IParameterSymbol parameter)
    {
        var direct = parameter.Name;
        var stripped = direct.StartsWith("On", StringComparison.Ordinal) && direct.Length > 2
            ? direct.Substring(2)
            : null;

        // The property decides which of the two names the generator used, so ask it first and only fall
        // back to trying both when the property cannot be found (an interface-forwarded parameter).
        var property = FindProperty(componentType, direct);
        if (property is not null)
        {
            var isDelegate = StripNullable(property.Type).TypeKind == TypeKind.Delegate;
            var preferred = isDelegate && stripped is not null ? stripped : direct;
            return Applies(componentType, preferred) ? preferred : null;
        }

        if (Applies(componentType, direct))
        {
            return direct;
        }

        return stripped is not null && Applies(componentType, stripped) ? stripped : null;
    }

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
            var receiver = candidate.Parameters[0].Type;

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
    public static bool HasParameterlessEntry(SemanticModel model, int position, string name) =>
        model.LookupSymbols(position, name: name)
            .OfType<IMethodSymbol>()
            .Any(m => m is { Parameters.Length: 0, IsStatic: true } && m.TypeParameters.Length > 0);

    /// <summary>
    ///     Whether a bound control's one-argument entry — <c>Input&lt;T&gt;(Expression&lt;Func&lt;T&gt;&gt;
    ///     Bind)</c> — is reachable here. That argument is what infers <c>T</c>, so a site already passing
    ///     one keeps it and turns everything else into setters.
    /// </summary>
    public static bool HasBindEntry(SemanticModel model, int position, string name) =>
        model.LookupSymbols(position, name: name)
            .OfType<IMethodSymbol>()
            .Any(m => m is { Parameters.Length: 1, IsStatic: true } && m.Parameters[0].Name == "Bind");

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
