using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using static Rask.Generators.CodeText;
using static Rask.Generators.ComponentSymbols;

namespace Rask.Generators;

public sealed partial class ComponentFactoryGenerator
{
    /// <summary>
    ///     The candidates one class declaration contributes: itself, or — for a package declaration
    ///     (<c>Mui : ReactPackage</c>) — one island per export, none of which is in source.
    /// </summary>
    private static EquatableArray<Candidate> GetCandidates(GeneratorSyntaxContext ctx)
    {
        if (ctx.Node is ClassDeclarationSyntax classDecl
            && ctx.SemanticModel.GetDeclaredSymbol(classDecl) is INamedTypeSymbol symbol
            && global::Rask.Generators.External.PackageIslands.PackageDeclarations.Read(symbol) is { } declaration)
        {
            return PackageCandidates(symbol, classDecl, declaration, ctx.SemanticModel.Compilation);
        }

        return GetCandidate(ctx) is { } candidate
            ? new EquatableArray<Candidate>(new[] { candidate })
            : default;
    }

    /// <summary>
    ///     A package declaration's islands as candidates: each is the runtime's base class — the same inherited surface
    ///     a declared <c>sealed partial class MuiButton : ReactComponent</c> would have — named for its export, with its
    ///     props from the snapshot (<c>WithPackageProps</c>)
    ///     and its entry on the declaration: <c>Mui.Button</c>.
    /// </summary>
    /// <remarks>
    ///     Synthesized rather than read, because the island class is written by <c>ExternalGenerator</c> and no
    ///     generator sees another's output. Both expand the declaration through <c>PackageDeclarations</c>, which is what
    ///     keeps the class and its chain describing one component.
    /// </remarks>
    private static EquatableArray<Candidate> PackageCandidates(
        INamedTypeSymbol declaration,
        ClassDeclarationSyntax classDecl,
        global::Rask.Generators.External.PackageIslands.PackageDeclaration read,
        Compilation compilation)
    {
        // The island generator declares nothing for a declaration it reports (RASK056, RASK059), so neither does this:
        // steps for a class that was never declared would bury the one real diagnostic under errors in generated code.
        var isPartial = declaration.DeclaringSyntaxReferences.Any(static r =>
            r.GetSyntax() is ClassDeclarationSyntax c && c.Modifiers.Any(SyntaxKind.PartialKeyword));
        if (!isPartial
            || read.Failed is not null
            || read.Module is not { } module
            || !global::Rask.Generators.External.PackageIslands.PackageSpecifier.IsBare(module)
            || global::Rask.Generators.External.PackageIslands.PackageDeclarations.RuntimeBase(compilation, read.Runtime)
                is not { } runtimeBase)
        {
            return default;
        }

        var ns = declaration.ContainingNamespace.IsGlobalNamespace
            ? string.Empty
            : declaration.ContainingNamespace.ToDisplayString();
        var properties = GetFactoryProperties(runtimeBase, false, compilation);
        var lifecycle = OverridesLifecycleHook(runtimeBase);
        var isPublic = IsExternallyVisible(declaration);
        var inherited = ReachableMemberNames(runtimeBase);

        var result = new List<Candidate>();
        foreach (var island in read.Islands)
        {
            var memberNames = new SortedSet<string>(inherited, StringComparer.Ordinal) { island.Name };
            result.Add(new Candidate(
                ns,
                island.Name,
                island.Name,
                ns.Length == 0 ? "global::" + island.Name : $"global::{ns}.{island.Name}",
                string.Empty,
                default,
                string.Empty,
                HasParameterlessCtor: true,
                HasDIConstructor: false,
                isPublic,
                GenericFactory: null,
                FormControl: null,
                new EquatableArray<PropInfo>(properties),
                IsPartial: true,
                IsNested: false,
                IsElement: false,
                lifecycle,
                classDecl.Identifier.GetLocation().SourceTree?.FilePath ?? string.Empty,
                classDecl.Identifier.Span.Start,
                classDecl.Identifier.Span.Length,
                $"<c>{Prose(island.Export)}</c> from <c>{Prose(module)}</c>.",
                new EquatableArray<string>(memberNames.ToArray()),
                default,
                true,
                global::Rask.Generators.External.PackageIslands.PackageDeclarations.Facts(
                    declaration, read, island, runtimeBase),
                GroupOn(declaration, island.Member)));
        }

        return new EquatableArray<Candidate>(result.ToArray());
    }

    // [FactoryGeneric] and [RaskChainEntry], the two attributes that shape a candidate's entry.
    private static (GenericFactoryConfig? GenericFactory, string? ChainEntry, List<TagInfo> Tags) ReadFactoryAttributes(
        INamedTypeSymbol symbol)
    {
        GenericFactoryConfig? genericFactory = null;
        string? chainEntry = null;
        var tags = new List<TagInfo>();
        foreach (var attr in symbol.GetAttributes())
        {
            if (string.Equals(attr.AttributeClass?.ToDisplayString(), FactoryGenericFullName, StringComparison.Ordinal))
            {
                genericFactory = ParseGenericFactoryConfig(attr);
            }
            else if (string.Equals(attr.AttributeClass?.ToDisplayString(), TagFullName, StringComparison.Ordinal)
                     && attr.ConstructorArguments.Length == 1
                     && attr.ConstructorArguments[0].Value is string { Length: > 0 } tag)
            {
                var entryArg = attr.NamedArguments
                    .FirstOrDefault(static a => string.Equals(a.Key, "Entry", StringComparison.Ordinal)).Value.Value as string;
                tags.Add(new TagInfo(tag, entryArg ?? char.ToUpperInvariant(tag[0]) + tag.Substring(1)));
            }
            else if (string.Equals(attr.AttributeClass?.ToDisplayString(), ChainEntryFullName, StringComparison.Ordinal)
                     && attr.ConstructorArguments.Length == 1
                     && attr.ConstructorArguments[0].Value is string entry
                     && entry.Length > 0)
            {
                chainEntry = entry;
            }
        }

        return (genericFactory, chainEntry, tags);
    }

    private static Candidate? GetCandidate(GeneratorSyntaxContext ctx)
    {
        if (ctx.Node is not ClassDeclarationSyntax classDecl
            || ctx.SemanticModel.GetDeclaredSymbol(classDecl) is not INamedTypeSymbol symbol
            || symbol.IsAbstract
            || symbol.IsUnboundGenericType
            || symbol.DeclaredAccessibility is not (Accessibility.Public or Accessibility.Internal)
            || !InheritsFromComponent(symbol)
            || IsInRaskCoreNamespace(symbol)
            || HasSkipFactoryAttribute(symbol))
        {
            return null;
        }

        var ns = symbol.ContainingNamespace.IsGlobalNamespace
            ? string.Empty
            : symbol.ContainingNamespace.ToDisplayString();
        var hasParameterlessCtor = HasPublicParameterlessConstructor(symbol);
        var hasDICtor = DeclaresDIConstructor(symbol);
        var isPublic = IsExternallyVisible(symbol);
        var formControl = GetFormControlInfo(symbol, ctx.SemanticModel.Compilation);
        var properties = GetFactoryProperties(symbol, formControl is not null, ctx.SemanticModel.Compilation);
        var typeParams = symbol.IsGenericType
            ? "<" + string.Join(", ", symbol.TypeParameters.Select(tp => tp.Name)) + ">"
            : string.Empty;
        var typeParamAnnotations = ReadTypeParameterAnnotations(symbol.TypeParameters);
        var constraints = BuildConstraintsClause(symbol.TypeParameters);
        var (genericFactory, chainEntry, tags) = ReadFactoryAttributes(symbol);

        return new Candidate(
            ns,
            symbol.Name,
            chainEntry ?? (tags.Count > 0 ? tags[0].Entry : symbol.Name),
            symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            typeParams,
            new EquatableArray<string>(typeParamAnnotations),
            constraints,
            hasParameterlessCtor,
            hasDICtor,
            isPublic,
            genericFactory,
            formControl,
            new EquatableArray<PropInfo>(properties),
            classDecl.Modifiers.Any(SyntaxKind.PartialKeyword),
            symbol.ContainingType is not null,
            InheritsFromElement(symbol),
            OverridesLifecycleHook(symbol),
            classDecl.Identifier.GetLocation().SourceTree?.FilePath ?? string.Empty,
            classDecl.Identifier.Span.Start,
            classDecl.Identifier.Span.Length,
            SummaryOf(symbol),
            ReachableMemberNames(symbol),
            EnclosingTypeHeaders(symbol),
            AllEnclosingPartial(symbol),
            global::Rask.Generators.External.PackageIslands.PackageIslandProps.Facts(symbol),
            ChainGroupOf(symbol, chainEntry ?? symbol.Name),
            new EquatableArray<TagInfo>(tags));
    }

    /// <summary>
    ///     The group <paramref name="symbol" />'s entry is added to, from the nearest
    ///     <c>[RaskChainGroup]</c> on it or a base class, or null when it has none that applies.
    /// </summary>
    /// <remarks>
    ///     Walked by hand because Roslyn's <c>GetAttributes()</c> returns only what a type declares itself, while the
    ///     attribute is meant to be written once, on a library's base class. Honoured only when the group is declared
    ///     in the component's OWN assembly: the entry is added by re-opening the group as a partial, which cannot be
    ///     done to another assembly's type — so an app component deriving from a library's base keeps its bare entry.
    /// </remarks>
    private static ChainGroup? ChainGroupOf(INamedTypeSymbol symbol, string entryName)
    {
        for (var t = symbol; t is not null; t = t.BaseType)
        {
            foreach (var attr in t.GetAttributes())
            {
                if (!string.Equals(attr.AttributeClass?.ToDisplayString(), ChainGroupFullName, StringComparison.Ordinal)
                    || attr.ConstructorArguments.Length == 0
                    || attr.ConstructorArguments[0].Value is not INamedTypeSymbol group)
                {
                    continue;
                }

                if (!SymbolEqualityComparer.Default.Equals(group.ContainingAssembly, symbol.ContainingAssembly))
                {
                    return null;
                }

                var member = attr.ConstructorArguments.Length > 1
                             && attr.ConstructorArguments[1].Value is string named && named.Length > 0
                    ? named
                    : GroupMemberName(entryName, group.Name);

                return GroupOn(group, member);
            }
        }

        // The assembly-wide form: a group for every component whose name carries the group's, so a library of fifty
        // `Ui*` components states it once. A component whose name does not (a `Card` beside `Ui.Card`) keeps its bare
        // entry rather than landing on the group under its whole name.
        foreach (var attr in symbol.ContainingAssembly.GetAttributes())
        {
            if (string.Equals(attr.AttributeClass?.ToDisplayString(), ChainGroupFullName, StringComparison.Ordinal)
                && attr.ConstructorArguments.Length != 0
                && attr.ConstructorArguments[0].Value is INamedTypeSymbol group
                && SymbolEqualityComparer.Default.Equals(group.ContainingAssembly, symbol.ContainingAssembly)
                && GroupMemberName(entryName, group.Name) is var member
                && !string.Equals(member, entryName, StringComparison.Ordinal))
            {
                return GroupOn(group, member);
            }
        }

        return null;
    }

    /// <summary>The entry <paramref name="member" /> on <paramref name="group" />, as the partial that re-opens it.</summary>
    private static ChainGroup GroupOn(INamedTypeSymbol group, string member)
    {
        var headers = new List<string>();
        var partial = true;
        for (var g = group; g is not null; g = g.ContainingType)
        {
            headers.Add($"{AccessibilityKeyword(g)}{(g.IsStatic ? "static " : string.Empty)}partial class {g.Name}");
            partial &= g.DeclaringSyntaxReferences.Any(static r =>
                r.GetSyntax() is TypeDeclarationSyntax decl && decl.Modifiers.Any(SyntaxKind.PartialKeyword));
        }

        headers.Reverse();
        return new ChainGroup(
            group.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            group.ContainingNamespace.IsGlobalNamespace ? string.Empty : group.ContainingNamespace.ToDisplayString(),
            new EquatableArray<string>(headers.ToArray()),
            member,
            partial);
    }

    /// <summary>
    ///     A component's name with its group's taken off the front or the back — <c>UiButton</c> in <c>Ui</c> is
    ///     <c>Button</c>, <c>FullscreenTrigger</c> in <c>Trigger</c> is <c>Fullscreen</c> — or the whole name when
    ///     neither leaves a name behind.
    /// </summary>
    internal static string GroupMemberName(string component, string group)
    {
        if (component.Length > group.Length && component.StartsWith(group, StringComparison.Ordinal)
                                              && char.IsUpper(component[group.Length]))
        {
            return component.Substring(group.Length);
        }

        if (component.Length > group.Length && component.EndsWith(group, StringComparison.Ordinal))
        {
            return component.Substring(0, component.Length - group.Length);
        }

        return component;
    }

    // Detects IFormControl<T> among the component's implemented interfaces and returns the bound value
    // type T (fully qualified). Null when the component is not a form control.
    private static FormControlInfo? GetFormControlInfo(INamedTypeSymbol symbol, Compilation compilation)
    {
        foreach (var i in symbol.AllInterfaces)
        {
            if (i.TypeArguments.Length == 1 &&
                string.Equals(i.OriginalDefinition.ToDisplayString(), FormControlOpenFullName, StringComparison.Ordinal))
            {
                var argument = i.TypeArguments[0];
                var valueType = DisplayTypeName(argument, FullyQualifiedNullable, compilation);
                var lifts = argument is { IsValueType: true, TypeKind: not TypeKind.TypeParameter }
                            && argument.OriginalDefinition.SpecialType != SpecialType.System_Nullable_T;
                var element = argument is INamedTypeSymbol
                {
                    TypeArguments.Length: 1,
                } collection
                    && string.Equals(collection.OriginalDefinition.ToDisplayString(), "System.Collections.Generic.ICollection<T>", StringComparison.Ordinal)
                        ? DisplayTypeName(collection.TypeArguments[0], FullyQualifiedNullable, compilation)
                        : null;
                return new FormControlInfo(valueType, lifts, element);
            }
        }

        return null;
    }

    private static GenericFactoryConfig? ParseGenericFactoryConfig(AttributeData attr)
    {
        if (attr.ConstructorArguments.Length == 0
            || attr.ConstructorArguments[0].Value is not string typeParameter
            || typeParameter.Length == 0)
        {
            return null;
        }

        var modelProperty = string.Empty;
        var constraint = "class";
        var typedDelegates = Array.Empty<string>();
        foreach (var named in attr.NamedArguments)
        {
            switch (named.Key)
            {
                case "ModelProperty":
                    if (named.Value.Value is string mp)
                    {
                        modelProperty = mp;
                    }

                    break;
                case "Constraint":
                    if (named.Value.Value is string ct && ct.Length > 0)
                    {
                        constraint = ct;
                    }

                    break;
                case "TypedDelegateProperties":
                    if (!named.Value.IsNull)
                    {
                        typedDelegates = named.Value.Values
                            .Select(v => v.Value as string)
                            .Where(s => !string.IsNullOrEmpty(s))
                            .Select(s => s!)
                            .ToArray();
                    }

                    break;
            }
        }

        return new GenericFactoryConfig(
            typeParameter,
            modelProperty,
            new EquatableArray<string>(typedDelegates),
            constraint);
    }

    private static string BuildConstraintsClause(ImmutableArray<ITypeParameterSymbol> typeParameters)
    {
        if (typeParameters.IsDefaultOrEmpty)
        {
            return string.Empty;
        }

        var sb = new StringBuilder();
        foreach (var tp in typeParameters)
        {
            var clauses = new List<string>();
            if (tp.HasReferenceTypeConstraint)
            {
                clauses.Add("class");
            }

            if (tp.HasValueTypeConstraint && !tp.HasUnmanagedTypeConstraint)
            {
                clauses.Add("struct");
            }

            if (tp.HasUnmanagedTypeConstraint)
            {
                clauses.Add("unmanaged");
            }

            if (tp.HasNotNullConstraint)
            {
                clauses.Add("notnull");
            }

            foreach (var ct in tp.ConstraintTypes)
            {
                clauses.Add(ct.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));
            }

            if (tp.HasConstructorConstraint)
            {
                clauses.Add("new()");
            }

            if (clauses.Count == 0)
            {
                continue;
            }

            sb.Append(" where ").Append(tp.Name).Append(" : ").Append(string.Join(", ", clauses));
        }

        return sb.ToString();
    }

    // Rask.Core.RaskMarkup is the builder surface and nothing else — Component's own base. A type that
    // names it DIRECTLY is a markup host: it wants to name markup without being a component, which is
    // what a test class, a fixture or a demo factory is.
    //
    // Directly, not transitively, and that is not a detail. A shared test base that derives from
    // RaskMarkup passes the framework entries down to every subclass by ordinary inheritance — but if
    // each of those subclasses were a host too, every one of them would need 'partial' (RASK036) the day
    // the base was changed, in files that name no markup at all. One edit to a base is not allowed to
    // become an error in fourteen untouched files. Injection is the expensive, opt-in half of the
    // surface, so it follows the declaration that opted in.
    private static bool DeclaresRaskMarkup(INamedTypeSymbol symbol) =>
        string.Equals(symbol.BaseType?.OriginalDefinition.ToDisplayString(), RaskMarkupFullName, StringComparison.Ordinal);

    // The attribute form of the same opt-in, for a type that cannot spend its base slot. Direct by
    // construction and not merely by policy: GetAttributes() returns what was written on THIS type's
    // declarations, never what a base carries — so the contagion the base-class form had to rule out by
    // hand cannot arise here at all.
    private static bool HasRaskMarkupAttribute(INamedTypeSymbol symbol) =>
        symbol.GetAttributes().Any(static attribute => string.Equals(
            attribute.AttributeClass?.ToDisplayString(), RaskMarkupAttributeFullName, StringComparison.Ordinal));

    // Does this level of a component's inheritance chain belong to the SHARED builder surface — the
    // props emitted once as constrained generic extensions (GetSetterHost) instead of per component?
    // That is exactly Rask.Core's Element/Component chain, which GetSetterHost walks from Element up to
    // object; the two must agree, or a prop is either emitted twice or not at all. Every other base a
    // component inherits from — HtmlMediaElement, BsBlock, BsFormControl<T>, a consumer's own base —
    // has no shared emission, so its props need a per-component setter or they get none.
    private static bool IsSharedSurfaceType(ITypeSymbol type)
    {
        var name = type.OriginalDefinition.ToDisplayString();
        return string.Equals(name, ElementFullName, StringComparison.Ordinal) || string.Equals(name, ComponentFullName, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Whether the component overrides any of <c>Component</c>'s own lifecycle hooks.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         This is what a builder entry hands to <c>Entry&lt;T&gt;</c> so the runtime knows whether the
    ///         child has anything to run when the deferred commit reaches it. A component that does gets
    ///         its <c>LiveState</c> claimed at build time, because the commit uses "no LiveState" to mean
    ///         "not mine to notify" and a handle-less render leaves one unallocated; a component that does
    ///         not is left alone, which is what keeps a page of plain tags from paying a LiveState apiece.
    ///     </para>
    ///     <para>
    ///         The hook set is read off the <c>Component</c> symbol rather than hard-coded — every virtual
    ///         <c>Task</c>-returning method it declares — so adding a hook to the framework cannot silently leave a component
    ///         uncommitted. <c>Element</c>-derived types are NOT exempt: <c>NavLink</c> is an Element and
    ///         overrides <c>OnMount</c>.
    ///     </para>
    /// </remarks>
    private static bool OverridesLifecycleHook(INamedTypeSymbol symbol)
    {
        INamedTypeSymbol? componentType = null;
        for (var t = symbol; t is not null; t = t.BaseType)
        {
            if (string.Equals(t.OriginalDefinition.ToDisplayString(), ComponentFullName, StringComparison.Ordinal))
            {
                componentType = t;
                break;
            }
        }

        // Not a component at all, or a shape this walk cannot see the base of: assume it has a lifecycle,
        // because the cost of being wrong that way is one allocation and the cost of the other way is a
        // component that never mounts.
        if (componentType is null)
        {
            return true;
        }

        // The hooks are exactly the virtual Task-returning methods Component declares — OnMount, OnUpdated,
        // OnFirstRender, OnRendered, OnUnmount. By SHAPE, not by name: the hooks used to share an `On` prefix, and
        // when they lost it a name test quietly matched nothing, so no component was reported as having a
        // lifecycle and a handle-less render skipped every Mount.
        var hooks = new HashSet<string>(StringComparer.Ordinal);
        foreach (var member in componentType.GetMembers())
        {
            if (member is IMethodSymbol { IsVirtual: true, Parameters.Length: 0 } m
                && string.Equals(m.ReturnType.ToDisplayString(), "System.Threading.Tasks.Task", StringComparison.Ordinal))
            {
                hooks.Add(m.Name);
            }
        }

        for (var t = symbol; t is not null && !SymbolEqualityComparer.Default.Equals(t, componentType); t = t.BaseType)
        {
            foreach (var member in t.GetMembers())
            {
                if (member is IMethodSymbol { IsOverride: true } m && hooks.Contains(m.Name))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool InheritsFromElement(INamedTypeSymbol symbol)
    {
        for (var t = symbol; t is not null; t = t.BaseType)
        {
            if (string.Equals(t.OriginalDefinition.ToDisplayString(), ElementFullName, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    // An event-callback delegate prop whose invocation should re-render its owner: an
    // Action/Action<T>/Func<Task>/Func<T,Task> shape (void- or Task-returning, arity <= 1). The
    // return-type rule excludes template/data delegates — Func<…,Component> (ErrorBoundary.Fallback),
    // Func<…,ValueTask<…>> (VirtualizeModel.ItemsProvider), Func<…,IEnumerable<…>>
    // (validators) — so only true parent→child callbacks are wrapped.
    private static bool IsAutoRerenderDelegateType(ITypeSymbol type)
    {
        if (type is not INamedTypeSymbol named || named.TypeKind != TypeKind.Delegate)
        {
            return false;
        }

        var invoke = named.DelegateInvokeMethod;
        if (invoke is null || invoke.Parameters.Length > 1)
        {
            return false;
        }

        var ret = invoke.ReturnType;
        if (ret.SpecialType == SpecialType.System_Void)
        {
            return true;
        }

        return string.Equals(
            ret.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            "global::System.Threading.Tasks.Task",
            StringComparison.Ordinal);
    }

    // The same question asked of a PROPERTY: lift a `Nullable<>` first, then ask the delegate — and
    // recognise a CARRIER, which is a struct holding its delegate and so answers the delegate question
    // with a flat no.
    //
    // Getting that wrong is silent in the worst way. A non-Element component's callback loses its
    // auto-rerender wrapping, the markup stays byte-identical, and the only symptom is that clicking the
    // thing no longer repaints the component whose state it just changed. This function has now lost the
    // unwrap twice — once when carriers were removed and the comment here was rewritten to say the lift
    // "is all that is left", and again when they came back — so it is pinned by
    // BuilderCallbackTests.A_component_callback_is_wrapped_where_an_element_controls_is_not.
    //
    // Only the `Callback` family counts. `Fn` and `Validator` return values and are called DURING a
    // render, so wrapping one to re-render would render from inside a render.
    private static bool IsAutoRerenderProp(ITypeSymbol type)
    {
        var lifted =
            type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } n
                ? n.TypeArguments[0]
                : type;

        var fqn = lifted.ToDisplayString(FullyQualifiedNullable);
        var open = fqn.IndexOf('<');
        if (string.Equals(open < 0 ? fqn : fqn.Substring(0, open), CallbackFqn, StringComparison.Ordinal))
        {
            return true;
        }

        return IsAutoRerenderDelegateType(lifted);
    }

    private static bool IsInRaskCoreNamespace(INamedTypeSymbol symbol)
    {
        var ns = symbol.ContainingNamespace?.ToDisplayString() ?? string.Empty;

        // Rask.Core itself (Component base, Text/Raw) and the Live runtime are excluded —
        // they are not user-facing tag wrappers. Except MDN's element types, which live in
        // Rask.Core beside MDN's event types: everything there that derives from Element.
        if (string.Equals(ns, "Rask.Core", StringComparison.Ordinal))
        {
            return !DerivesFromElement(symbol);
        }

        if (string.Equals(ns, "Rask.Core.Live", StringComparison.Ordinal) || ns.StartsWith("Rask.Core.Live.", StringComparison.Ordinal))
        {
            return true;
        }

        return false;
    }

    private static bool DerivesFromElement(INamedTypeSymbol symbol)
    {
        for (var t = symbol.BaseType; t is not null; t = t.BaseType)
        {
            if (string.Equals(t.ToDisplayString(), "Rask.Core.Element", StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasSkipFactoryAttribute(ISymbol symbol) =>
        symbol.GetAttributes().Any(static attr => string.Equals(
            attr.AttributeClass?.ToDisplayString(), SkipFactoryFullName, StringComparison.Ordinal));

    private static bool HasPublicParameterlessConstructor(INamedTypeSymbol symbol)
    {
        foreach (var ctor in symbol.InstanceConstructors)
        {
            if (ctor.DeclaredAccessibility == Accessibility.Public && ctor.Parameters.Length == 0)
            {
                return true;
            }
        }

        return false;
    }

    private static bool DeclaresDIConstructor(INamedTypeSymbol symbol)
    {
        foreach (var ctor in symbol.InstanceConstructors)
        {
            if (ctor.DeclaredAccessibility == Accessibility.Public && ctor.Parameters.Length > 0)
            {
                return true;
            }
        }

        return false;
    }

    private static List<PropInfo> GetFactoryProperties(INamedTypeSymbol symbol, bool isFormControl,
        Compilation compilation)
    {
        var result = new List<PropInfo>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        // Element subclasses (Button, Input, Form, …) forward their delegate props straight onto a
        // DOM element, where handler-owner resolution already re-renders the parent for free. Only
        // plain (non-Element) components host parent↔child callbacks that need auto-wrapping; this
        // also keeps the render hot path (and the CounterAllocationPin) free of wrapper closures.
        var isElement = InheritsFromElement(symbol);

        // Walk the inheritance chain (most-derived first). Properties on a derived type
        // shadow same-name properties on a base — the `seen` set enforces "first wins" so
        // user shadows beat Component's defaults. `depth` records each property's distance
        // from the most-derived type so the final sort can keep derived-class properties
        // ahead of inherited ones (tag-specific first, then Id/Class/Style/Data). The
        // Children property is filtered out below — it's reached via the indexer, not a
        // factory parameter.
        var depth = 0;
        for (var current = symbol;
             current is not null && current.SpecialType != SpecialType.System_Object;
             current = current.BaseType, depth++)
        {
            foreach (var member in current.GetMembers())
            {
                // A name already seen is shadowed by a more-derived declaration we already visited.
                if (member is IPropertySymbol prop && seen.Add(prop.Name) && IsFactoryProperty(prop))
                {
                    result.Add(ToPropInfo(prop, current, depth, isElement, isFormControl, compilation));
                }
            }
        }

        AppendBlazorParameters(result, symbol);
        SortByDeclaration(result);
        return result;
    }

    private static bool IsFactoryProperty(IPropertySymbol prop) =>
        !prop.IsStatic && !prop.IsIndexer && !prop.IsImplicitlyDeclared
        && prop.DeclaredAccessibility == Accessibility.Public
        && prop.SetMethod is { DeclaredAccessibility: Accessibility.Public }
        && !HasSkipFactoryAttribute(prop)
        && !IsOverrideOfRaskCoreMember(prop)
        // Children is exposed via the `Component this[params Component[]]` indexer, not as
        // a factory parameter. Skip any property that matches the standard Children shape
        // so subclasses can't accidentally bring it back into the factory signature.
        && !(string.Equals(prop.Name, "Children", StringComparison.Ordinal) && IsChildCollectionType(
            prop.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat
                .WithMiscellaneousOptions(
                    SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier
                    | SymbolDisplayMiscellaneousOptions.UseSpecialTypes))));

    private static PropInfo ToPropInfo(
        IPropertySymbol prop, INamedTypeSymbol declaring, int depth, bool isElement, bool isFormControl,
        Compilation compilation)
    {
        var (filePath, spanStart, spanLength, hasInitializer, initializerDefault) = ReadDeclaration(prop, compilation);

        // A non-nullable `Callback` counts: its default is an unset slot, so it is optional rather
        // than a required step (see CallbackCarrier).
        var isNullable = prop.Type.NullableAnnotation == NullableAnnotation.Annotated
                         || (prop.Type.IsValueType && prop.Type.OriginalDefinition.SpecialType ==
                             SpecialType.System_Nullable_T)
                         || CallbackCarrier.IsNonNullable(prop.Type);

        var typeFqn = DisplayTypeName(prop.Type, FullyQualifiedNullable, compilation);

        // A bound-mode IFormControl<T> member (Bind/Validate/AfterBind):
        // excluded from the controlled factory and instead emitted on the synthesized bound factory.
        var isBoundInterfaceProp = isFormControl &&
                                   Array.IndexOf(BoundInterfaceMembers, prop.Name) >= 0;

        // An unconstrained type-parameter prop (e.g. `TValue? Value`) can't default to `null` —
        // there's no conversion from null to T — so its optional factory param must default to
        // `default`. (A `class`/`notnull`-constrained T would accept null, but `default` is always
        // valid, so key off the type-parameter kind rather than the constraint set.)
        var isTypeParameter = prop.Type is ITypeParameterSymbol;

        var (enumType, enumMembers) = EnumStepSource(prop.Type);

        return new PropInfo(
            prop.Name,
            typeFqn,
            isNullable,
            hasInitializer,
            prop.IsRequired,
            depth,
            filePath,
            spanStart,
            spanLength,
            IsAutoRerenderDelegateProp(prop, isElement, isBoundInterfaceProp),
            isTypeParameter,
            isBoundInterfaceProp,
            IsDelegateProp(prop, compilation),
            initializerDefault,
            prop.SetMethod?.IsInitOnly == true,
            IsSharedSurfaceType(declaring),
            DeclaresDerivedSetter(prop),
            SummaryOf(prop),
            IsSensitiveProp(prop),
            enumType,
            enumMembers);
    }

    // Where the property is declared, and what its member initializer says. A constant member initializer
    // (`= "x"`, `= BsColor.Danger`) becomes the factory param's DEFAULT value instead of excluding the
    // property; non-constant initializers (`= new List<>()`) stay excluded. Formatted for the generated file
    // (no usings there). Restricted to a regular `set` accessor: an `init`-only property cannot be reassigned
    // post-construction, and the factory reassigns every param on the reused persisted-component path
    // (`__c.Prop = prop;`), so promoting an init-only initializer to a param would emit code that fails
    // CS8852. Init-only-with-initializer stays excluded (as before).
    private static (string FilePath, int SpanStart, int SpanLength, bool HasInitializer, string? InitializerDefault)
        ReadDeclaration(IPropertySymbol prop, Compilation compilation)
    {
        if (prop.DeclaringSyntaxReferences.Length == 0)
        {
            return (string.Empty, 0, 0, false, null);
        }

        var syntaxRef = prop.DeclaringSyntaxReferences[0];
        var filePath = syntaxRef.SyntaxTree.FilePath ?? string.Empty;
        if (syntaxRef.GetSyntax() is not PropertyDeclarationSyntax pds)
        {
            return (filePath, syntaxRef.Span.Start, syntaxRef.Span.Length, false, null);
        }

        string? initializerDefault = null;
        if (pds.Initializer is { } init && prop.SetMethod?.IsInitOnly != true)
        {
            var constant = compilation.GetSemanticModel(init.SyntaxTree).GetConstantValue(init.Value);
            if (constant.HasValue)
            {
                initializerDefault = FormatConstantDefault(constant.Value, prop.Type);
            }
        }

        return (filePath, syntaxRef.Span.Start, syntaxRef.Span.Length, pds.Initializer is not null, initializerDefault);
    }

    // AfterBind/AfterBindAsync are Action<T>/Func<T,Task>-shaped, so they would qualify for
    // auto-wrapping on a non-Element control (BsInput, BsSelect, …) — but they are post-bind
    // hooks, not event callbacks, and the bound factory has always assigned them raw. Excluding
    // every bound member here keeps the builder setters on the same rule.
    // An Element subclass forwards its delegate props straight to the DOM, where
    // handler-owner resolution already repaints and a wrap would only add a hot-path closure
    // — so they are assigned verbatim (DelegatePropOnElementSubclass_IsNotWrapped).
    //
    // [AutoCallback] is the exception, and it exists because Form needs one: its submit
    // handlers are NOT dispatched by the DOM, they are invoked by Form's own submit bridge
    // after validation, so nothing else would repaint the component that owns them. That is
    // what [FactoryGeneric]'s TypedDelegateProperties used to say, on a component that is no
    // longer generic-by-factory.
    private static bool IsAutoRerenderDelegateProp(IPropertySymbol prop, bool isElement, bool isBoundInterfaceProp) =>
        (!isElement || HasAutoCallbackAttribute(prop))
        && !isBoundInterfaceProp && IsAutoRerenderProp(prop.Type);

    // Any delegate-typed prop (event callbacks: Callback/CallbackAsync/Action/Func) is
    // excluded from the propsChanged fold — two delegates/closures are practically never
    // equal, so folding them forces propsChanged: true every render (defeating the render
    // cache) AND emits per-prop snapshot+compare bookkeeping that scales with the count. The
    // universal GlobalEventHandlers surface adds ~50 delegate props to every element factory,
    // so this is load-bearing for the render-hotpath allocation pin. Distinct from
    // isAutoRerenderDelegate, which ALSO drives the parent re-render wrapping that element
    // props must NOT get.
    // A CARRIER counts too, and missing that is the quietest regression in this file: a
    // `Callback` — or `Callback?`, `Nullable<Callback>` — is a STRUCT, so the TypeKind test alone says false and
    // every carrier-typed handler starts folding. Nothing fails — every element carrying a
    // handler simply reports propsChanged: true on every frame, and the render cache is
    // defeated tree-wide. Only the allocation benchmarks would notice.
    private static bool IsDelegateProp(IPropertySymbol prop, Compilation compilation) =>
        prop.Type is INamedTypeSymbol { TypeKind: TypeKind.Delegate }
        || CarrierDelegates(DisplayTypeName(prop.Type, FullyQualifiedNullable, compilation)).Count > 0;

    // A Blazor island's chain steps come from the component it HOSTS rather than from anything it
    // declares — not redeclaring the hosted component's surface is the point of the feature.
    //
    // Appended HERE, in the generator that emits the setters, rather than generated as properties
    // by BlazorGenerator and picked up on some later pass: one source generator never sees
    // another's output, so a property written there would be invisible to this and would get no
    // chain step at all. Both read BlazorParameters.Read so the list cannot diverge — whatever
    // emits a property must be matched by whatever emits its setter.
    private static void AppendBlazorParameters(List<PropInfo> result, INamedTypeSymbol symbol)
    {
        if (Blazor.BlazorParameters.HostedTypeOf(symbol) is not { } hostedComponent)
        {
            return;
        }

        var islandRef = symbol.DeclaringSyntaxReferences.FirstOrDefault();
        var islandPath = islandRef?.SyntaxTree.FilePath ?? string.Empty;
        var islandStart = islandRef?.Span.Start ?? 0;
        var islandLength = islandRef?.Span.Length ?? 0;

        // Against what was actually PRODUCED, not against the walk's `seen`. That set records every
        // property name walked, including ones immediately skipped for being static — and the
        // chain entries inherited from RaskMarkup are static members named after components,
        // so consulting it would silently drop a parameter called Text, Table, Form or Label.
        // Those are ordinary names for a UI library, and the failure would be no step, no
        // diagnostic, and no way to pass a value the component plainly declares.
        foreach (var hosted in Blazor.BlazorParameters.Read(symbol, hostedComponent)
                     .Where(h => !result.Any(p => string.Equals(p.Name, h.Name, StringComparison.Ordinal))))
        {
            result.Add(new PropInfo(
                hosted.Name,
                hosted.ChainTypeFqn,
                // Optional unless the hosted component said otherwise. Defaulting to nullable is
                // what keeps every call site from having to supply every parameter the component
                // happens to declare; [EditorRequired] is Blazor's own way of saying a parameter
                // is mandatory, so it maps onto Rask's required step and nothing else does.
                IsNullable: !hosted.IsRequired,
                HasInitializer: false,
                UserMarkedRequired: hosted.IsRequired,
                InheritanceDepth: 0,
                islandPath,
                islandStart,
                islandLength,
                // An EventCallback becomes a plain delegate, and a callback prop on a non-Element
                // component is auto-wrapped so invoking it re-renders the owning parent.
                IsAutoRerenderDelegate: hosted.IsEventCallback,
                IsTypeParameter: false,
                IsBoundInterfaceProp: false,
                IsDelegate: hosted.IsEventCallback,
                InitializerDefault: null,
                IsInitOnly: false,
                IsSharedSurfaceProp: false,
                HasDerivedSetter: false,
                $"Feeds the hosted component's <c>{hosted.Parameter}</c> parameter."));
        }
    }

    // Sort: (a) derived-class properties first (lowest depth), then (b) by file path
    // and span — preserves the user's declaration order within each level of the
    // inheritance chain.
    private static void SortByDeclaration(List<PropInfo> result) =>
        result.Sort(static (a, b) =>
        {
            var d = a.InheritanceDepth.CompareTo(b.InheritanceDepth);
            if (d != 0)
            {
                return d;
            }

            var c = string.CompareOrdinal(a.DeclaringFilePath, b.DeclaringFilePath);
            return c != 0 ? c : a.DeclaringSpanStart.CompareTo(b.DeclaringSpanStart);
        });

    private static bool IsOverrideOfRaskCoreMember(IPropertySymbol prop)
    {
        if (!prop.IsOverride)
        {
            return false;
        }

        var overridden = prop.OverriddenProperty;
        while (overridden is not null)
        {
            var ns = overridden.ContainingType.ContainingNamespace?.ToDisplayString() ?? string.Empty;
            if (string.Equals(ns, "Rask.Core", StringComparison.Ordinal) || ns.StartsWith("Rask.Core.", StringComparison.Ordinal))
            {
                return true;
            }

            overridden = overridden.OverriddenProperty;
        }

        return false;
    }

    private static string DefaultLiteralFor(PropInfo p)
    {
        // A property with a constant member initializer contributes its value as the param default.
        if (p.InitializerDefault is { } init)
            return init;

        // Otherwise the optional set is exactly the nullable props (a non-nullable prop with no
        // initializer is a required factory param with no default). A type-parameter prop must use
        // `default` — `null` has no conversion to an unconstrained T.
        //
        // So must a base's `T? Value` closed over a STRUCT (`UiFormField<DateOnly>`): the property keeps its
        // annotation, so it is optional, but `T?` on an unconstrained T is not Nullable<T> — the substituted type
        // is plain `DateOnly`, which `null` does not convert to. Every type that CAN hold null is written with
        // its `?` (an annotated reference type, or Nullable<T>), so the missing `?` is what gives it away.
        return p.IsNullable && !p.IsTypeParameter && p.TypeFqn.EndsWith("?", StringComparison.Ordinal)
            ? "null"
            : "default";
    }

    // The same rule straight off the symbol, for the shared Element/Component surface — which is
    // collected from symbols (GetSetterHost) rather than as PropInfo. A constant member initializer is
    // the value an omitted factory parameter carries, so it is what a reset has to restore.
    private static (string Literal, bool IsRequired) DefaultLiteralFor(IPropertySymbol p, Compilation compilation)
    {
        var hasInitializer = false;
        if (p.DeclaringSyntaxReferences.Length > 0
            && p.DeclaringSyntaxReferences[0].GetSyntax() is PropertyDeclarationSyntax { Initializer: { } init })
        {
            hasInitializer = true;
            if (p.SetMethod?.IsInitOnly == false)
            {
                var constant = compilation.GetSemanticModel(init.SyntaxTree).GetConstantValue(init.Value);
                if (constant.HasValue && FormatConstantDefault(constant.Value, p.Type) is { } literal)
                {
                    return (literal, false);
                }
            }
        }

        var isNullable = p.Type.NullableAnnotation == NullableAnnotation.Annotated
                         || (p.Type.IsValueType
                             && p.Type.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T)
                         || CallbackCarrier.IsNonNullable(p.Type);
        // A struct reached through an annotated `T?` (a generic base closed over DateOnly) is optional, but holds no
        // null — see the PropInfo overload above.
        var holdsNull = !p.Type.IsValueType
                        || p.Type.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T;
        return (isNullable && holdsNull && p.Type is not ITypeParameterSymbol ? "null" : "default",
            !isNullable && !hasInitializer);
    }

    // Formats a constant initializer value as a C# default-parameter literal usable in the generated
    // file (which has no `using`s): enums cast from their underlying constant to the fully-qualified
    // type; strings/chars use escaped literals; floating/long values carry their type suffix.
    private static string? FormatConstantDefault(object? value, ITypeSymbol type)
    {
        if (value is null)
            return "null";

        var underlying = type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } n
            ? n.TypeArguments[0]
            : type;

        if (underlying.TypeKind == TypeKind.Enum)
        {
            var enumFqn = underlying.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            return "(" + enumFqn + ")" +
                   Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture);
        }

        return value switch
        {
            string s => Microsoft.CodeAnalysis.CSharp.SymbolDisplay.FormatLiteral(s, true),
            char ch => Microsoft.CodeAnalysis.CSharp.SymbolDisplay.FormatLiteral(ch, true),
            bool b => b ? "true" : "false",
            float f => f.ToString(System.Globalization.CultureInfo.InvariantCulture) + "F",
            double d => d.ToString(System.Globalization.CultureInfo.InvariantCulture) + "D",
            decimal m => m.ToString(System.Globalization.CultureInfo.InvariantCulture) + "M",
            long l => l.ToString(System.Globalization.CultureInfo.InvariantCulture) + "L",
            ulong ul => ul.ToString(System.Globalization.CultureInfo.InvariantCulture) + "UL",
            _ => Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture)
        };
    }

    private static bool IsChildCollectionType(string typeFqn)
    {
        // Strip ALL nullable annotations (the outer collection `?` and the inner `Component?`
        // element annotation) so the match holds regardless of how nullability is rendered.
        var t = typeFqn.Replace("?", "");
        return t is "global::System.Collections.Generic.IEnumerable<global::Rask.Core.Component>"
            or "global::System.Collections.Generic.IReadOnlyList<global::Rask.Core.Component>"
            or "global::System.Collections.Generic.IReadOnlyCollection<global::Rask.Core.Component>"
            or "global::System.Collections.Generic.IList<global::Rask.Core.Component>"
            or "global::System.Collections.Generic.ICollection<global::Rask.Core.Component>"
            or "global::System.Collections.Generic.List<global::Rask.Core.Component>"
            or "global::Rask.Core.Component[]";
    }

    private static string StripNullable(string typeFqn) =>
        typeFqn.EndsWith("?", StringComparison.Ordinal)
            ? typeFqn.Substring(0, typeFqn.Length - 1)
            : typeFqn;

    private static bool IsRequiredFactoryParam(PropInfo p) =>
        !p.IsNullable && !p.HasInitializer;

    private static bool IsParamProperty(PropInfo p) =>
        // A property with a constant member initializer is an optional param defaulting to that value;
        // a non-constant initializer (InitializerDefault == null) is still excluded entirely.
        !p.HasInitializer || p.InitializerDefault is not null;

    private static Location MakeLocation(PropInfo p)
    {
        if (string.IsNullOrEmpty(p.DeclaringFilePath))
        {
            return Location.None;
        }

        return Location.Create(
            p.DeclaringFilePath,
            new TextSpan(p.DeclaringSpanStart, p.DeclaringSpanLength),
            new LinePositionSpan(new LinePosition(0, 0), new LinePosition(0, 0)));
    }

    // A property/parameter name as a valid C# identifier in emitted code. ISymbol.Name strips the
    // leading '@' from a verbatim identifier (a property declared `@event` has Name "event"), so a
    // reserved keyword must be re-escaped with '@' wherever it is emitted as an identifier —
    // otherwise the generated factory (`string? event = null`, `__c.event = event`) fails to
    // compile in the consumer's build. Use this only for emitted identifiers; comparisons against
    // metadata names (modelProperty, "Children", typed-delegate sets) keep the raw Name.
    internal static string EscapeIdentifier(string name) =>
        SyntaxFacts.GetKeywordKind(name) != SyntaxKind.None ? "@" + name : name;
}
