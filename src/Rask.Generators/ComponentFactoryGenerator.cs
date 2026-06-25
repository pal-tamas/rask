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

namespace Rask.Generators;

[Generator(LanguageNames.CSharp)]
public sealed class ComponentFactoryGenerator : IIncrementalGenerator
{
    private const string ComponentFullName = "Rask.Core.Component";
    private const string ElementFullName = "Rask.Core.Element";
    private const string SkipFactoryFullName = "Rask.Core.SkipFactoryAttribute";
    private const string FactoryGenericFullName = "Rask.Core.FactoryGenericAttribute";
    private const string GenerateForwarderFactoryFullName = "Rask.Core.GenerateForwarderFactoryAttribute";
    private const string ContextFullName = "global::Rask.Core.Live.LiveRenderContext";

    private static readonly DiagnosticDescriptor Rask001 = new(
        "RASK001",
        "Property is treated as a required factory parameter",
        "Property '{0}.{1}' is treated as a required factory parameter; consider also marking it 'required' for language-level enforcement",
        "Rask.Generators",
        DiagnosticSeverity.Hidden,
        true,
        helpLinkUri: DiagnosticHelp.Link("RASK001"));

    private static readonly DiagnosticDescriptor Rask002 = new(
        "RASK002",
        "'required' property cannot be honored by the generated factory",
        "Property '{0}.{1}' is marked 'required', but the generated factory for '{0}' cannot set it: '{0}' has a dependency-injected constructor and the property is either excluded from the factory parameters (it has a member initializer) or only reachable via ActivatorUtilities.CreateInstance (no parameterless constructor). Adding a parameterless constructor does not help while the DI constructor remains — the factory then builds '{0}' with 'new {0}()' and the DI constructor never runs, leaving injected services null. Remove 'required', move the value to a constructor parameter (with no initializer), or drop the DI constructor.",
        "Rask.Generators",
        DiagnosticSeverity.Warning,
        true,
        helpLinkUri: DiagnosticHelp.Link("RASK002"));

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var candidates = context.SyntaxProvider
            .CreateSyntaxProvider(
                static (node, _) => node is ClassDeclarationSyntax c && c.BaseList is { Types.Count: > 0 } &&
                                    !c.Modifiers.Any(m => m.IsKind(SyntaxKind.AbstractKeyword)),
                static (ctx, _) => GetCandidate(ctx))
            .Where(static c => c is not null)
            .Select(static (c, _) => c!);

        var grouped = candidates.Collect();

        // RaskFactoryNavigation (default true) gates the per-factory `<see cref>` doc breadcrumb
        // that links each generated factory to its component type (Quick-Doc / hover navigation;
        // F12 still resolves to the generated method — Roslyn/ReSharper navigate generated symbols
        // to the generated file, so use "Navigate to Type of Symbol" for a one-action jump to the
        // component). `[DebuggerStepThrough]` is emitted unconditionally so the debugger always
        // steps over the factory into user code.
        var navigationEnabled = context.AnalyzerConfigOptionsProvider.Select(static (p, _) =>
            !p.GlobalOptions.TryGetValue("build_property.RaskFactoryNavigation", out var v)
            || !string.Equals(v, "false", StringComparison.OrdinalIgnoreCase));

        context.RegisterSourceOutput(grouped.Combine(navigationEnabled),
            static (spc, t) => Emit(spc, t.Left, t.Right));

        var globalUsingsEnabled = context.AnalyzerConfigOptionsProvider.Select(static (p, _) =>
            !p.GlobalOptions.TryGetValue("build_property.RaskGlobalUsings", out var v)
            || !string.Equals(v, "false", StringComparison.OrdinalIgnoreCase));

        var globalUsingsInput = grouped.Combine(globalUsingsEnabled);
        context.RegisterSourceOutput(globalUsingsInput,
            static (spc, t) => EmitGlobalUsings(spc, t.Left, t.Right));
    }

    private static Candidate? GetCandidate(GeneratorSyntaxContext ctx)
    {
        if (ctx.Node is not ClassDeclarationSyntax classDecl)
        {
            return null;
        }

        if (ctx.SemanticModel.GetDeclaredSymbol(classDecl) is not INamedTypeSymbol symbol)
        {
            return null;
        }

        if (symbol.IsAbstract)
        {
            return null;
        }

        if (symbol.IsUnboundGenericType)
        {
            return null;
        }

        if (symbol.DeclaredAccessibility != Accessibility.Public &&
            symbol.DeclaredAccessibility != Accessibility.Internal)
        {
            return null;
        }

        if (!InheritsFromComponent(symbol))
        {
            return null;
        }

        if (IsInRaskCoreNamespace(symbol))
        {
            return null;
        }

        if (HasSkipFactoryAttribute(symbol))
        {
            return null;
        }

        var ns = symbol.ContainingNamespace.IsGlobalNamespace
            ? string.Empty
            : symbol.ContainingNamespace.ToDisplayString();
        var hasParameterlessCtor = HasPublicParameterlessConstructor(symbol);
        var hasDICtor = HasDIConstructor(symbol);
        var isPublic = IsExternallyVisible(symbol);
        var properties = GetFactoryProperties(symbol);
        var typeParams = symbol.IsGenericType
            ? "<" + string.Join(", ", symbol.TypeParameters.Select(tp => tp.Name)) + ">"
            : string.Empty;
        var constraints = BuildConstraintsClause(symbol.TypeParameters);
        GenericFactoryConfig? genericFactory = null;
        foreach (var attr in symbol.GetAttributes())
        {
            if (attr.AttributeClass?.ToDisplayString() == FactoryGenericFullName)
            {
                genericFactory = ParseGenericFactoryConfig(attr);
            }
        }

        var forwarders = GetForwarderInfos(symbol);
        return new Candidate(
            ns,
            symbol.Name,
            symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            typeParams,
            constraints,
            hasParameterlessCtor,
            hasDICtor,
            isPublic,
            genericFactory,
            new EquatableArray<PropInfo>(properties),
            new EquatableArray<ForwarderInfo>(forwarders));
    }

    private static List<ForwarderInfo> GetForwarderInfos(INamedTypeSymbol symbol)
    {
        var result = new List<ForwarderInfo>();
        foreach (var member in symbol.GetMembers())
        {
            if (member is not IMethodSymbol method)
            {
                continue;
            }

            if (!method.IsStatic || method.DeclaredAccessibility != Accessibility.Public)
            {
                continue;
            }

            var hasAttr = false;
            string? validatorParam = null;
            foreach (var attr in method.GetAttributes())
            {
                if (attr.AttributeClass?.ToDisplayString() == GenerateForwarderFactoryFullName)
                {
                    hasAttr = true;
                    foreach (var named in attr.NamedArguments)
                    {
                        if (named.Key == "Validator" && named.Value.Value is string v && v.Length > 0)
                        {
                            validatorParam = v;
                        }
                    }

                    break;
                }
            }

            if (!hasAttr)
            {
                continue;
            }

            // The validator fan-out types its sync/async overloads as Validate<T>/ValidateAsync<T>, where T
            // is the bound value type — derived from the `Expression<Func<T>>` Bind parameter. That's the
            // method's own type parameter for Input.Bound<TProp> (Expression<Func<TProp>>) and a concrete
            // constructed type for MultiSelect.Bound (Expression<Func<ICollection<TItem>>>).
            var validatorTypeArg = ExtractBoundValueType(method);

            var typeParams = method.TypeParameters.Length > 0
                ? "<" + string.Join(", ", method.TypeParameters.Select(tp => tp.Name)) + ">"
                : string.Empty;
            var constraints = BuildConstraintsClause(method.TypeParameters);

            var parameters = new List<ForwarderParamInfo>();
            foreach (var p in method.Parameters)
            {
                var typeFqn = p.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat
                    .WithMiscellaneousOptions(SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier
                                              | SymbolDisplayMiscellaneousOptions.UseSpecialTypes));
                var defaultLiteral = string.Empty;
                if (p.HasExplicitDefaultValue)
                {
                    defaultLiteral = TryGetDefaultLiteralFromSyntax(p) ?? FormatDefaultLiteral(p.ExplicitDefaultValue);
                }

                parameters.Add(new ForwarderParamInfo(typeFqn, p.Name, defaultLiteral, p.IsParams));
            }

            result.Add(new ForwarderInfo(
                method.Name,
                typeParams,
                constraints,
                new EquatableArray<ForwarderParamInfo>(parameters),
                validatorTypeArg.Length > 0 ? validatorParam : null,
                validatorTypeArg));
        }

        return result;
    }

    private static string? TryGetDefaultLiteralFromSyntax(IParameterSymbol p)
    {
        if (p.DeclaringSyntaxReferences.Length == 0)
        {
            return null;
        }

        if (p.DeclaringSyntaxReferences[0].GetSyntax() is not ParameterSyntax syntax)
        {
            return null;
        }

        var value = syntax.Default?.Value;
        return value?.ToString();
    }

    private static string FormatDefaultLiteral(object? value)
    {
        if (value is null)
        {
            return "null";
        }

        return value switch
        {
            bool b => b ? "true" : "false",
            string s => "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"",
            char c => "'" + c + "'",
            IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
            _ => value.ToString() ?? "default"
        };
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
        var typedValidators = Array.Empty<string>();
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
                case "TypedValidatorProperties":
                    if (!named.Value.IsNull)
                    {
                        typedValidators = named.Value.Values
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
            new EquatableArray<string>(typedValidators),
            constraint);
    }

    // Finds the bound value type T from a method's `Expression<Func<T>>` parameter (the Bind expression),
    // fully qualified. Returns "" when no such parameter exists. Used to type the validator fan-out's
    // Validate<T>/ValidateAsync<T> overloads.
    private static string ExtractBoundValueType(IMethodSymbol method)
    {
        foreach (var p in method.Parameters)
        {
            if (p.Type is INamedTypeSymbol { Name: "Expression", TypeArguments.Length: 1 } expr
                && expr.TypeArguments[0] is INamedTypeSymbol { Name: "Func", TypeArguments.Length: 1 } func)
            {
                return func.TypeArguments[0].ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat
                    .WithMiscellaneousOptions(SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier
                                              | SymbolDisplayMiscellaneousOptions.UseSpecialTypes));
            }
        }

        return string.Empty;
    }

    // Merges two `<...>` type-parameter lists into one. Either may be empty; when both are present (a
    // generic method on a generic component) they're concatenated: "<A>" + "<B>" → "<A, B>".
    private static string MergeTypeParams(string a, string b)
    {
        if (a.Length == 0)
        {
            return b;
        }

        if (b.Length == 0)
        {
            return a;
        }

        return a.Substring(0, a.Length - 1) + ", " + b.Substring(1);
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

    private static bool IsExternallyVisible(INamedTypeSymbol symbol)
    {
        for (var t = symbol; t is not null; t = t.ContainingType)
        {
            if (t.DeclaredAccessibility != Accessibility.Public)
            {
                return false;
            }
        }

        return true;
    }

    private static bool InheritsFromComponent(INamedTypeSymbol symbol)
    {
        for (var t = symbol.BaseType; t is not null; t = t.BaseType)
        {
            var name = t.OriginalDefinition.ToDisplayString();
            if (name == ComponentFullName)
            {
                return true;
            }
        }

        return false;
    }

    private static bool InheritsFromElement(INamedTypeSymbol symbol)
    {
        for (var t = symbol; t is not null; t = t.BaseType)
        {
            if (t.OriginalDefinition.ToDisplayString() == ElementFullName)
            {
                return true;
            }
        }

        return false;
    }

    // An event-callback delegate prop whose invocation should re-render its owner: an
    // Action/Action<T>/Func<Task>/Func<T,Task> shape (void- or Task-returning, arity <= 1). The
    // return-type rule excludes template/data delegates — Func<…,Component>, Func<…,ValueTask<…>>
    // (VirtualizeModel.ItemsProvider), Func<…,Child> (ErrorBoundary.Fallback), Func<…,IEnumerable<…>>
    // (validators) — so only true parent→child callbacks are wrapped.
    private static bool IsAutoRerenderDelegate(ITypeSymbol type)
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

        return ret.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
               == "global::System.Threading.Tasks.Task";
    }

    private static bool IsInRaskCoreNamespace(INamedTypeSymbol symbol)
    {
        var ns = symbol.ContainingNamespace?.ToDisplayString() ?? string.Empty;

        // Rask.Core itself (Component base, Text/Raw) and the Live runtime are excluded —
        // they are not user-facing tag wrappers. Rask.Core.Components is intentionally NOT
        // excluded: that is where the HTML tag wrappers live, and the generator now emits
        // their factories the same way it does for user components.
        if (ns == "Rask.Core")
        {
            return true;
        }

        if (ns == "Rask.Core.Live" || ns.StartsWith("Rask.Core.Live.", StringComparison.Ordinal))
        {
            return true;
        }

        return false;
    }

    private static bool HasSkipFactoryAttribute(ISymbol symbol)
    {
        foreach (var attr in symbol.GetAttributes())
        {
            if (attr.AttributeClass?.ToDisplayString() == SkipFactoryFullName)
            {
                return true;
            }
        }

        return false;
    }

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

    private static bool HasDIConstructor(INamedTypeSymbol symbol)
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

    private static List<PropInfo> GetFactoryProperties(INamedTypeSymbol symbol)
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
                if (member is not IPropertySymbol prop)
                {
                    continue;
                }

                if (!seen.Add(prop.Name))
                {
                    // Shadowed by a more-derived declaration we already visited.
                    continue;
                }

                if (prop.IsStatic || prop.IsIndexer || prop.IsImplicitlyDeclared)
                {
                    continue;
                }

                if (prop.DeclaredAccessibility != Accessibility.Public)
                {
                    continue;
                }

                if (prop.SetMethod is null)
                {
                    continue;
                }

                if (prop.SetMethod.DeclaredAccessibility != Accessibility.Public)
                {
                    continue;
                }

                if (HasSkipFactoryAttribute(prop))
                {
                    continue;
                }

                if (IsOverrideOfRaskCoreMember(prop))
                {
                    continue;
                }

                // Children is exposed via the `Component this[params Child[]]` indexer, not as
                // a factory parameter. Skip any property that matches the standard Children shape
                // so subclasses can't accidentally bring it back into the factory signature.
                if (prop.Name == "Children" && IsChildCollectionType(
                        prop.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat
                            .WithMiscellaneousOptions(
                                SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier
                                | SymbolDisplayMiscellaneousOptions.UseSpecialTypes))))
                {
                    continue;
                }

                var filePath = string.Empty;
                var spanStart = 0;
                var spanLength = 0;
                var hasInitializer = false;
                if (prop.DeclaringSyntaxReferences.Length > 0)
                {
                    var syntaxRef = prop.DeclaringSyntaxReferences[0];
                    filePath = syntaxRef.SyntaxTree.FilePath ?? string.Empty;
                    spanStart = syntaxRef.Span.Start;
                    spanLength = syntaxRef.Span.Length;
                    if (syntaxRef.GetSyntax() is PropertyDeclarationSyntax pds)
                    {
                        hasInitializer = pds.Initializer is not null;
                    }
                }

                var isNullable = prop.Type.NullableAnnotation == NullableAnnotation.Annotated
                                 || (prop.Type.IsValueType && prop.Type.OriginalDefinition.SpecialType ==
                                     SpecialType.System_Nullable_T);

                var typeFqn = prop.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat
                    .WithMiscellaneousOptions(SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier
                                              | SymbolDisplayMiscellaneousOptions.UseSpecialTypes));

                var isAutoRerenderDelegate = !isElement && IsAutoRerenderDelegate(prop.Type);

                result.Add(new PropInfo(
                    prop.Name,
                    typeFqn,
                    isNullable,
                    hasInitializer,
                    prop.IsRequired,
                    depth,
                    filePath,
                    spanStart,
                    spanLength,
                    isAutoRerenderDelegate));
            }
        }

        // Sort: (a) derived-class properties first (lowest depth), then (b) by file path
        // and span — preserves the user's declaration order within each level of the
        // inheritance chain.
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
        return result;
    }

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
            if (ns == "Rask.Core" || ns.StartsWith("Rask.Core.", StringComparison.Ordinal))
            {
                return true;
            }

            overridden = overridden.OverriddenProperty;
        }

        return false;
    }

    private static string DefaultLiteralFor(PropInfo p)
    {
        // Only optional params reach this; the optional set is exactly the nullable props (a
        // non-nullable prop with no initializer is a required factory param with no default).
        return p.IsNullable ? "null" : "default";
    }

    private static void Emit(SourceProductionContext spc, ImmutableArray<Candidate> candidates, bool emitNavigation)
    {
        if (candidates.IsDefaultOrEmpty)
        {
            return;
        }

        // Report per-property diagnostics first.
        foreach (var c in candidates)
        {
            foreach (var p in c.Properties)
            {
                var location = MakeLocation(p);
                // A parameterless ctor only rescues `required` props that are actually factory
                // params. A `required` property carrying a member initializer is excluded from the
                // factory params (IsParamProperty), so the generated object initializer never
                // assigns it and the consumer build fails with CS9035 — keep warning in that case.
                if (p.UserMarkedRequired && c.HasDIConstructor && (!c.HasParameterlessCtor || p.HasInitializer))
                {
                    spc.ReportDiagnostic(Diagnostic.Create(Rask002, location, c.FullyQualifiedName, p.Name));
                }
                else if (IsRequiredFactoryParam(p) && !p.UserMarkedRequired)
                {
                    spc.ReportDiagnostic(Diagnostic.Create(Rask001, location, c.FullyQualifiedName, p.Name));
                }
            }
        }

        var byNamespace = candidates
            .GroupBy(c => c.Namespace)
            .OrderBy(g => g.Key, StringComparer.Ordinal);

        foreach (var group in byNamespace)
        {
            var sb = new StringBuilder();
            sb.AppendLine("// <auto-generated />");
            sb.AppendLine("#nullable enable");
            sb.AppendLine();

            var hasNs = !string.IsNullOrEmpty(group.Key);
            if (hasNs)
            {
                sb.Append("namespace ").Append(group.Key).AppendLine(";");
                sb.AppendLine();
            }

            sb.AppendLine("public static partial class Generated");
            sb.AppendLine("{");

            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var c in group.OrderBy(c => c.TypeName, StringComparer.Ordinal))
            {
                if (!seen.Add(c.TypeName))
                {
                    continue;
                }

                EmitFactory(sb, c, emitNavigation);
                sb.AppendLine();

                if (c.GenericFactory is { } gf)
                {
                    EmitGenericFactoryOverload(sb, c, gf, emitNavigation);
                    sb.AppendLine();
                }

                foreach (var f in c.Forwarders)
                {
                    EmitForwarderFactory(sb, c, f, emitNavigation);
                    sb.AppendLine();
                }
            }

            sb.AppendLine("}");

            var hint = hasNs ? $"{group.Key}.Generated.g.cs" : "Generated.g.cs";
            spc.AddSource(hint, SourceText.From(sb.ToString(), Encoding.UTF8));
        }
    }

    // Children is delivered via the `Component this[params Child[]]` indexer on Component
    // itself — the factory has no Children parameter. This helper exists only to recognize
    // the standard Children collection shapes while filtering them out in GetFactoryProperties.
    private static bool IsChildCollectionType(string typeFqn)
    {
        var t = StripNullable(typeFqn);
        return t is "global::System.Collections.Generic.IEnumerable<global::Rask.Core.Child>"
            or "global::System.Collections.Generic.IReadOnlyList<global::Rask.Core.Child>"
            or "global::System.Collections.Generic.IReadOnlyCollection<global::Rask.Core.Child>"
            or "global::System.Collections.Generic.IList<global::Rask.Core.Child>"
            or "global::System.Collections.Generic.ICollection<global::Rask.Core.Child>"
            or "global::System.Collections.Generic.List<global::Rask.Core.Child>"
            or "global::Rask.Core.Child[]";
    }

    private static string StripNullable(string typeFqn) =>
        typeFqn.EndsWith("?", StringComparison.Ordinal)
            ? typeFqn.Substring(0, typeFqn.Length - 1)
            : typeFqn;

    private static bool IsRequiredFactoryParam(PropInfo p) =>
        !p.IsNullable && !p.HasInitializer;

    private static bool IsParamProperty(PropInfo p) =>
        !p.HasInitializer; // properties with initializers are excluded entirely

    // Emits the per-factory header trivia: a `<see cref>` doc breadcrumb that links to the
    // component type (Quick-Doc / hover navigation; gated by RaskFactoryNavigation) and an
    // always-on `[DebuggerStepThrough]` so the debugger steps over the factory into user code.
    // F12 still lands on the generated method — stock Roslyn/ReSharper navigate a generated
    // symbol to its generated document; "Navigate to Type of Symbol" jumps to the component.
    private static void EmitMethodHeader(StringBuilder sb, Candidate c, bool emitNavigation)
    {
        if (emitNavigation)
        {
            // cref uses `{T}` (not `<T>`) for generic arity — the doc-comment cref syntax — so it
            // resolves to the component type and renders as a navigable link.
            var cref = c.FullyQualifiedName.Replace('<', '{').Replace('>', '}');
            sb.Append("    /// <summary>Factory for the <see cref=\"").Append(cref)
                .AppendLine("\"/> component.</summary>");
        }

        sb.AppendLine("    [global::System.Diagnostics.DebuggerStepThrough]");
    }

    private static void EmitFactory(StringBuilder sb, Candidate c, bool emitNavigation)
    {
        var visibility = c.IsPublic ? "public" : "internal";
        var paramProps = c.Properties.Where(IsParamProperty).ToList();
        var requiredProps = paramProps.Where(IsRequiredFactoryParam).ToList();
        var optionalProps = paramProps.Where(p => !IsRequiredFactoryParam(p)).ToList();

        // Key (Component-level, Blazor @key parity) is a reconciliation IDENTITY, not a reactive
        // prop: it's a factory param and is assigned to the instance, but it's excluded from the
        // propsChanged diff. That keeps a propertyless component on the `propsChanged: false` fast
        // path, and means a Key change never fires OnPropsChanged (a different key is a different
        // logical item, which mounts fresh rather than re-rendering the old instance).
        var hasKeyProp = paramProps.Any(IsKeyProp);

        // nonKeyProps need re-assignment every render (drives the full-vs-fast path choice).
        // foldProps is the subset that participates in the propsChanged diff: it also excludes
        // auto-wrapped event-callback delegates, which are a fresh wrapper closure every render
        // (never meaningfully equal — diffing them is pure noise that would defeat the
        // `propsChanged: false` fast path for any callback-receiving component). They are still
        // assigned each render, just not folded.
        var nonKeyProps = paramProps.Where(p => !IsKeyProp(p)).ToList();
        var foldProps = nonKeyProps.Where(p => !p.IsAutoRerenderDelegate).ToList();

        // Prefer the parameterless ctor + object-initializer path whenever it's available.
        // Even if the component declares additional ctors that take services (DI) or
        // primitives (Text/Raw's string-arg ctor), the generated factory only needs the
        // parameterless one and then assigns properties — no ActivatorUtilities required.
        var canUseObjectInit = c.HasParameterlessCtor || !c.HasDIConstructor;

        // Signature.
        EmitMethodHeader(sb, c, emitNavigation);
        sb.Append("    ").Append(visibility).Append(" static ").Append(c.FullyQualifiedName).Append(' ')
            .Append(c.TypeName).Append(c.TypeParameters).Append('(');
        var first = true;
        foreach (var p in requiredProps)
        {
            if (!first)
            {
                sb.Append(", ");
            }

            first = false;
            sb.Append(p.TypeFqn).Append(' ').Append(p.Escaped);
        }

        foreach (var p in optionalProps)
        {
            if (!first)
            {
                sb.Append(", ");
            }

            first = false;
            sb.Append(p.TypeFqn).Append(' ').Append(p.Escaped).Append(" = ")
                .Append(DefaultLiteralFor(p));
        }

        sb.Append(')').AppendLine(c.TypeParameterConstraints);
        sb.AppendLine("    {");

        if (nonKeyProps.Count == 0)
        {
            // Legacy parameterless factory shape preserved (Key, if present, is assigned but not
            // diffed — so this fast path still emits propsChanged: false).
            sb.Append("        if (").Append(ContextFullName).AppendLine(".Current is { } __ctx)");
            sb.AppendLine("        {");
            sb.Append("            var __c = __ctx.GetOrCreate<").Append(c.FullyQualifiedName).AppendLine(">(");
            if (canUseObjectInit)
            {
                // Prefer the parameterless ctor: a context with no service provider (tests
                // calling RenderAsLiveRoot() without a ServiceProvider) would otherwise NRE
                // inside ActivatorUtilities. The DI-ctor branch below stays as a fallback for
                // components whose only constructors take injected services.
                sb.Append("                static _ => new ").Append(c.FullyQualifiedName).AppendLine("());");
            }
            else
            {
                sb.Append(
                        "                static __sp => global::Microsoft.Extensions.DependencyInjection.ActivatorUtilities.CreateInstance<")
                    .Append(c.FullyQualifiedName).AppendLine(">(__sp));");
            }

            if (hasKeyProp)
            {
                sb.AppendLine("            __c.Key = Key;");
            }

            sb.AppendLine("            __ctx.NotifyParameters(__c, propsChanged: false);");
            sb.AppendLine("            return __c;");
            sb.AppendLine("        }");
            if (c.HasParameterlessCtor)
            {
                if (hasKeyProp)
                {
                    sb.Append("        var __cf = new ").Append(c.FullyQualifiedName).AppendLine("();");
                    sb.AppendLine("        __cf.Key = Key;");
                    sb.AppendLine("        return __cf;");
                }
                else
                {
                    sb.Append("        return new ").Append(c.FullyQualifiedName).AppendLine("();");
                }
            }
            else
            {
                sb.Append("        throw new global::System.InvalidOperationException(\"Component '")
                    .Append(c.FullyQualifiedName)
                    .AppendLine(
                        "' has no parameterless constructor; it can only be instantiated inside a LiveRenderContext (e.g. via MapRask<TApp>).\");");
            }

            sb.AppendLine("    }");
            return;
        }

        // Has factory-param properties. Construct, then re-apply props every render so cached instances get fresh values.
        sb.Append("        ").Append(c.FullyQualifiedName).AppendLine(" __c;");
        sb.Append("        if (").Append(ContextFullName).AppendLine(".Current is { } __ctx)");
        if (canUseObjectInit)
        {
            // No DI ctor: closure captures args and seeds via object initializer (also satisfies `required`).
            sb.Append("            __c = __ctx.GetOrCreate<").Append(c.FullyQualifiedName).AppendLine(">(");
            sb.Append("                __sp => new ").Append(c.FullyQualifiedName).Append("()");
            EmitInitializerBody(sb, paramProps);
            sb.AppendLine(");");
        }
        else
        {
            sb.Append("            __c = __ctx.GetOrCreate<").Append(c.FullyQualifiedName).AppendLine(">(");
            sb.Append(
                    "                static __sp => global::Microsoft.Extensions.DependencyInjection.ActivatorUtilities.CreateInstance<")
                .Append(c.FullyQualifiedName).AppendLine(">(__sp));");
        }

        sb.AppendLine("        else");
        if (canUseObjectInit)
        {
            sb.Append("            __c = new ").Append(c.FullyQualifiedName).Append("()");
            EmitInitializerBody(sb, paramProps);
            sb.AppendLine(";");
        }
        else
        {
            sb.Append("            throw new global::System.InvalidOperationException(\"Component '")
                .Append(c.FullyQualifiedName)
                .AppendLine(
                    "' has no parameterless constructor; it can only be instantiated inside a LiveRenderContext (e.g. via MapRask<TApp>).\");");
        }

        EmitSnapshotsAndAssignments(sb, paramProps, foldProps);
        sb.Append("        if (").Append(ContextFullName).AppendLine(".Current is { } __ctx2)");
        sb.AppendLine("            __ctx2.NotifyParameters(__c, __propsChanged);");
        sb.AppendLine("        return __c;");
        sb.AppendLine("    }");
    }

    private static void EmitForwarderFactory(StringBuilder sb, Candidate c, ForwarderInfo f, bool emitNavigation)
    {
        // No validator configured → one verbatim forwarder (the original behavior).
        if (f.ValidatorParam is null)
        {
            EmitForwarderOverload(sb, c, f, emitNavigation, ValidatorShape.None, fanOut: false);
            return;
        }

        // Validator configured → fan into none/sync/async, exactly like the [FactoryGeneric] validator
        // fan-out, but forwarding to the hand-written source method instead of building the component.
        EmitForwarderOverload(sb, c, f, emitNavigation, ValidatorShape.None, fanOut: true);
        EmitForwarderOverload(sb, c, f, emitNavigation, ValidatorShape.Sync, fanOut: true);
        EmitForwarderOverload(sb, c, f, emitNavigation, ValidatorShape.Async, fanOut: true);
    }

    private static void EmitForwarderOverload(
        StringBuilder sb, Candidate c, ForwarderInfo f, bool emitNavigation, ValidatorShape shape, bool fanOut)
    {
        var visibility = c.IsPublic ? "public" : "internal";

        // The generated factory method carries BOTH the component's and the method's type parameters: a
        // generic method on a non-generic component (Input.Bound<TProp>) contributes the method's; a
        // non-generic method on a generic component (MultiSelect<TItem>.Bound) contributes the component's.
        // The call receiver is c.FullyQualifiedName (already constructed with the component's args), and the
        // method is invoked with only its own type args (f.TypeParameters) — so both shapes forward right.
        var factoryTypeParams = MergeTypeParams(c.TypeParameters, f.TypeParameters);
        var factoryConstraints = (c.TypeParameterConstraints + f.TypeParameterConstraints);

        // Signature: `public static {Component} {ComponentName}<...>(<params>) <constraints>`
        EmitMethodHeader(sb, c, emitNavigation);
        sb.Append("    ").Append(visibility).Append(" static ").Append(c.FullyQualifiedName).Append(' ')
            .Append(c.TypeName).Append(factoryTypeParams).Append('(');
        var first = true;
        for (var i = 0; i < f.Parameters.Count; i++)
        {
            var p = f.Parameters[i];
            var isValidator = fanOut && p.Name == f.ValidatorParam;

            // The None overload drops the validator parameter entirely (it's forwarded as null below).
            if (isValidator && shape == ValidatorShape.None)
            {
                continue;
            }

            if (!first)
            {
                sb.Append(", ");
            }

            first = false;
            if (p.IsParams)
            {
                sb.Append("params ");
            }

            if (isValidator && shape == ValidatorShape.Sync)
            {
                sb.Append("global::Rask.Core.Forms.Validate<").Append(f.ValidatorTypeArg).Append("> ").Append(p.Name);
            }
            else if (isValidator && shape == ValidatorShape.Async)
            {
                sb.Append("global::Rask.Core.Forms.ValidateAsync<").Append(f.ValidatorTypeArg).Append("> ")
                    .Append(p.Name);
            }
            else
            {
                sb.Append(p.TypeFqn).Append(' ').Append(p.Name);
                if (p.DefaultLiteral.Length > 0)
                {
                    sb.Append(" = ").Append(p.DefaultLiteral);
                }
            }
        }

        sb.Append(')').AppendLine(factoryConstraints);

        // Body forwards to the source method. Non-fan-out keeps positional forwarding (verbatim shape);
        // fan-out forwards by name so the omitted validator can be passed as null at its real position.
        sb.Append("        => ").Append(c.FullyQualifiedName).Append('.').Append(f.MethodName)
            .Append(f.TypeParameters).Append('(');
        for (var i = 0; i < f.Parameters.Count; i++)
        {
            if (i > 0)
            {
                sb.Append(", ");
            }

            var p = f.Parameters[i];
            if (!fanOut)
            {
                sb.Append(p.Name);
            }
            else if (p.Name == f.ValidatorParam && shape == ValidatorShape.None)
            {
                sb.Append(p.Name).Append(": null");
            }
            else
            {
                sb.Append(p.Name).Append(": ").Append(p.Name);
            }
        }

        sb.AppendLine(");");
    }

    private static void EmitGenericFactoryOverload(StringBuilder sb, Candidate c, GenericFactoryConfig gf,
        bool emitNavigation)
    {
        var visibility = c.IsPublic ? "public" : "internal";
        var typedSet = new HashSet<string>(StringComparer.Ordinal);
        var typedDelegates = new List<string>();
        foreach (var name in gf.TypedDelegateProperties)
        {
            if (string.IsNullOrEmpty(name) || !typedSet.Add(name))
            {
                continue;
            }

            typedDelegates.Add(name);
        }

        var typedValidators = new List<string>();
        var validatorSet = new HashSet<string>(StringComparer.Ordinal);
        foreach (var name in gf.TypedValidatorProperties)
        {
            if (string.IsNullOrEmpty(name) || !typedSet.Add(name))
            {
                continue;
            }

            typedValidators.Add(name);
            validatorSet.Add(name);
        }

        var modelProperty = gf.ModelProperty;
        var paramProps = c.Properties.Where(IsParamProperty).ToList();

        if (typedValidators.Count == 0)
        {
            EmitOneOverload(sb, c, gf, visibility, typedSet, typedDelegates, typedValidators, validatorSet,
                modelProperty, paramProps, ValidatorShape.None, emitNavigation);
            return;
        }

        // Fan out into three overloads. Overload resolution at the call site disambiguates:
        //   - no `Validate:` arg          → None overload (Validate forwarded as null)
        //   - one-arg lambda `v => …`     → Sync overload (typed Validate<T>)
        //   - two-arg lambda `(v, ct) => …` → Async overload (typed ValidateAsync<T>)
        // Both Sync and Async overloads make the validator parameter required so the No
        // overload remains the unambiguous match when the caller passes neither.
        EmitOneOverload(sb, c, gf, visibility, typedSet, typedDelegates, typedValidators, validatorSet,
            modelProperty, paramProps, ValidatorShape.None, emitNavigation);
        EmitOneOverload(sb, c, gf, visibility, typedSet, typedDelegates, typedValidators, validatorSet,
            modelProperty, paramProps, ValidatorShape.Sync, emitNavigation);
        EmitOneOverload(sb, c, gf, visibility, typedSet, typedDelegates, typedValidators, validatorSet,
            modelProperty, paramProps, ValidatorShape.Async, emitNavigation);
    }

    private static void EmitOneOverload(
        StringBuilder sb,
        Candidate c,
        GenericFactoryConfig gf,
        string visibility,
        HashSet<string> typedSet,
        List<string> typedDelegates,
        List<string> typedValidators,
        HashSet<string> validatorSet,
        string modelProperty,
        List<PropInfo> paramProps,
        ValidatorShape validatorShape,
        bool emitNavigation)
    {
        EmitMethodHeader(sb, c, emitNavigation);
        sb.Append("    ").Append(visibility).Append(" static ").Append(c.FullyQualifiedName).Append(' ')
            .Append(c.TypeName).Append('<').Append(gf.TypeParameter).Append(">(");

        var first = true;

        // Required: TModel Model — replaces ModelProperty's optional position with a typed,
        // mandatory parameter. The non-generic factory's `object? Model = null` accepts the
        // TModel value via implicit reference conversion (the `class` constraint ensures it).
        if (modelProperty.Length > 0)
        {
            first = false;
            sb.Append(gf.TypeParameter).Append(' ').Append(modelProperty);
        }

        // Typed validator parameter — required, no default. Position is right after Model so
        // it stays prominent in IntelliSense. Sync vs async is fixed per overload; the body
        // forwards the lambda into the non-generic factory's `Delegate?` slot.
        if (validatorShape == ValidatorShape.Sync)
        {
            foreach (var vp in typedValidators)
            {
                if (!first)
                {
                    sb.Append(", ");
                }

                first = false;
                sb.Append("global::Rask.Core.Forms.Validate<").Append(gf.TypeParameter).Append("> ").Append(vp);
            }
        }
        else if (validatorShape == ValidatorShape.Async)
        {
            foreach (var vp in typedValidators)
            {
                if (!first)
                {
                    sb.Append(", ");
                }

                first = false;
                sb.Append("global::Rask.Core.Forms.ValidateAsync<").Append(gf.TypeParameter).Append("> ").Append(vp);
            }
        }

        // Typed delegates: `Action<TModel>? X = null` then `Func<TModel, Task>? XAsync = null`,
        // grouped by side (sync first then async) for readability.
        foreach (var dp in typedDelegates)
        {
            if (!first)
            {
                sb.Append(", ");
            }

            first = false;
            sb.Append("global::Rask.Core.Callback<").Append(gf.TypeParameter).Append(">? ").Append(dp).Append(" = null");
        }

        foreach (var dp in typedDelegates)
        {
            if (!first)
            {
                sb.Append(", ");
            }

            first = false;
            sb.Append("global::Rask.Core.CallbackAsync<").Append(gf.TypeParameter)
                .Append(">? ").Append(dp).Append("Async = null");
        }

        // Remaining props in declaration order, skipping the Model and typed-delegate names
        // already covered above. Children is excluded by GetFactoryProperties.
        foreach (var p in paramProps)
        {
            if (p.Name == modelProperty || typedSet.Contains(p.Name))
            {
                continue;
            }

            if (!first)
            {
                sb.Append(", ");
            }

            first = false;
            sb.Append(p.TypeFqn).Append(' ').Append(p.Name);
            if (!IsRequiredFactoryParam(p))
            {
                sb.Append(" = ").Append(DefaultLiteralFor(p));
            }
        }

        sb.Append(") where ").Append(gf.TypeParameter).Append(" : ").AppendLine(gf.Constraint);
        sb.AppendLine("    {");

        foreach (var dp in typedDelegates)
        {
            sb.Append("        var __").Append(dp).Append(" = (global::System.Delegate?)").Append(dp)
                .Append(" ?? ").Append(dp).AppendLine("Async;");
        }

        sb.Append("        return ").Append(c.TypeName).AppendLine("(");
        var argLines = new List<string>();
        foreach (var p in paramProps)
        {
            string forward;
            if (p.Name == modelProperty)
            {
                forward = $"{p.Name}: {modelProperty}";
            }
            else if (validatorSet.Contains(p.Name))
            {
                forward = validatorShape == ValidatorShape.None
                    ? $"{p.Name}: null"
                    : $"{p.Name}: {p.Name}";
            }
            else if (typedSet.Contains(p.Name))
            {
                forward = $"{p.Name}: __{p.Name}";
            }
            else
            {
                forward = $"{p.Name}: {p.Name}";
            }

            argLines.Add(forward);
        }

        for (var i = 0; i < argLines.Count; i++)
        {
            sb.Append("            ").Append(argLines[i]);
            if (i < argLines.Count - 1)
            {
                sb.Append(',');
            }

            sb.AppendLine();
        }

        sb.AppendLine("        );");
        sb.AppendLine("    }");
    }

    private static void EmitInitializerBody(StringBuilder sb, IEnumerable<PropInfo> props)
    {
        sb.AppendLine();
        sb.AppendLine("            {");
        var entries = props.ToList();
        for (var i = 0; i < entries.Count; i++)
        {
            var p = entries[i];
            sb.Append("                ").Append(p.Escaped).Append(" = ").Append(p.Escaped);
            if (i < entries.Count - 1)
            {
                sb.Append(',');
            }

            sb.AppendLine();
        }

        sb.Append("            }");
    }

    // assignProps: every factory param re-applied to the (possibly cached) instance each render —
    // includes Key and auto-wrapped delegates. foldProps: the subset that participates in the
    // propsChanged diff — excludes Key (a reconciliation identity) and auto-wrapped delegates
    // (a fresh wrapper closure each render).
    private static void EmitSnapshotsAndAssignments(StringBuilder sb,
        IReadOnlyList<PropInfo> assignProps, IReadOnlyList<PropInfo> foldProps)
    {
        // Snapshot prior values of the diff-participating props (typed via the property's FQN so
        // nullable annotations round-trip).
        foreach (var p in foldProps)
        {
            // __old_<Name> is a fresh local (raw Name is a valid identifier even when Name is a
            // keyword); the property access __c.<Name> must be '@'-escaped.
            sb.Append("        var __old_").Append(p.Name).Append(" = __c.").Append(p.Escaped).AppendLine(";");
        }

        // Re-apply ALL params (including Key) so cached instances see fresh values. Event-callback
        // delegates are wrapped so invoking them re-renders the owning component (see AutoCallback).
        foreach (var p in assignProps)
        {
            sb.Append("        __c.").Append(p.Escaped).Append(" = ");
            if (p.IsAutoRerenderDelegate)
            {
                // Wrap returns a nullable delegate (null in → null out); a non-nullable prop never
                // passes null, so the null-forgiving `!` is safe and silences CS8601.
                sb.Append("global::Rask.Core.AutoCallback.Wrap(").Append(p.Escaped).Append(')');
                if (!p.IsNullable)
                {
                    sb.Append('!');
                }
            }
            else
            {
                sb.Append(p.Escaped);
            }

            sb.AppendLine(";");
        }

        if (foldProps.Count == 0)
        {
            sb.AppendLine("        var __propsChanged = false;");
            return;
        }

        // Fold per-prop equality into a single __propsChanged bool. EqualityComparer<T>.Default
        // gives ref-equality for ref types unless the type overrides Equals, and structural for
        // primitives — same semantics Blazor uses for [Parameter] equality.
        if (foldProps.Count == 1)
        {
            var p = foldProps[0];
            sb.Append("        var __propsChanged = !global::System.Collections.Generic.EqualityComparer<")
                .Append(p.TypeFqn).Append(">.Default.Equals(__old_").Append(p.Name).Append(", ").Append(p.Escaped)
                .AppendLine(");");
            return;
        }

        sb.AppendLine("        var __propsChanged =");
        for (var i = 0; i < foldProps.Count; i++)
        {
            var p = foldProps[i];
            sb.Append("            !global::System.Collections.Generic.EqualityComparer<").Append(p.TypeFqn)
                .Append(">.Default.Equals(__old_").Append(p.Name).Append(", ").Append(p.Escaped).Append(')');
            sb.AppendLine(i < foldProps.Count - 1 ? " ||" : ";");
        }
    }

    private static bool IsKeyProp(PropInfo p) => string.Equals(p.Name, "Key", StringComparison.Ordinal);

    private static void EmitGlobalUsings(
        SourceProductionContext spc,
        ImmutableArray<Candidate> candidates,
        bool enabled)
    {
        if (!enabled)
        {
            return;
        }

        var namespaces = candidates.IsDefaultOrEmpty
            ? Array.Empty<string>()
            : candidates
                .Select(c => c.Namespace)
                .Where(ns => !string.IsNullOrEmpty(ns))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(ns => ns, StringComparer.Ordinal)
                .ToArray();

        var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated />");
        // The framework's own factory namespaces — always make them globally visible to
        // consumers, even if this assembly defines no user components of its own (and so
        // `namespaces` is empty).
        sb.AppendLine("global using static global::Rask.Core.Components.Generated;");
        sb.AppendLine("global using static global::Rask.Core.Routing.Generated;");
        foreach (var ns in namespaces)
        {
            if (ns == "Rask.Core.Components" || ns == "Rask.Core.Routing")
            {
                continue;
            }

            sb.Append("global using static global::").Append(ns).AppendLine(".Generated;");
        }

        spc.AddSource("RaskGlobalUsings.g.cs", SourceText.From(sb.ToString(), Encoding.UTF8));
    }

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

    private enum ValidatorShape { None, Sync, Async }

    private sealed record Candidate(
        string Namespace,
        string TypeName,
        string FullyQualifiedName,
        string TypeParameters,
        string TypeParameterConstraints,
        bool HasParameterlessCtor,
        bool HasDIConstructor,
        bool IsPublic,
        GenericFactoryConfig? GenericFactory,
        EquatableArray<PropInfo> Properties,
        EquatableArray<ForwarderInfo> Forwarders);

    private readonly record struct GenericFactoryConfig(
        string TypeParameter,
        string ModelProperty,
        EquatableArray<string> TypedDelegateProperties,
        EquatableArray<string> TypedValidatorProperties,
        string Constraint);

    private readonly record struct ForwarderInfo(
        string MethodName,
        string TypeParameters,
        string TypeParameterConstraints,
        EquatableArray<ForwarderParamInfo> Parameters,
        string? ValidatorParam,
        string ValidatorTypeArg);

    private readonly record struct ForwarderParamInfo(
        string TypeFqn,
        string Name,
        string DefaultLiteral,
        bool IsParams);

    private readonly record struct PropInfo(
        string Name,
        string TypeFqn,
        bool IsNullable,
        bool HasInitializer,
        bool UserMarkedRequired,
        int InheritanceDepth,
        string DeclaringFilePath,
        int DeclaringSpanStart,
        int DeclaringSpanLength,
        bool IsAutoRerenderDelegate)
    {
        // The factory-parameter / property identifier, '@'-escaped when Name is a reserved keyword.
        public string Escaped => EscapeIdentifier(Name);
    }
}

internal readonly struct EquatableArray<T> : IEquatable<EquatableArray<T>>, IEnumerable<T>
    where T : IEquatable<T>
{
    private readonly T[] _items;

    public EquatableArray(IEnumerable<T> items) => _items = items?.ToArray() ?? Array.Empty<T>();

    public int Count => _items?.Length ?? 0;

    public T this[int index] => _items[index];

    public bool Equals(EquatableArray<T> other)
    {
        var a = _items ?? Array.Empty<T>();
        var b = other._items ?? Array.Empty<T>();
        if (a.Length != b.Length)
        {
            return false;
        }

        for (var i = 0; i < a.Length; i++)
        {
            if (!a[i].Equals(b[i]))
            {
                return false;
            }
        }

        return true;
    }

    public override bool Equals(object? obj) => obj is EquatableArray<T> other && Equals(other);

    public override int GetHashCode()
    {
        unchecked
        {
            var hash = 17;
            var arr = _items ?? Array.Empty<T>();
            foreach (var item in arr)
            {
                hash = (hash * 31) + item.GetHashCode();
            }

            return hash;
        }
    }

    public IEnumerator<T> GetEnumerator()
    {
        var arr = _items ?? Array.Empty<T>();
        return ((IEnumerable<T>)arr).GetEnumerator();
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
