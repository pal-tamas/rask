using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;

namespace Rask.Generators;

public sealed partial class RoutesGenerator
{
    private static void EmitRouteFactory(SourceProductionContext spc, StringBuilder sb, StringBuilder? extSb,
        Candidate c, Dictionary<string, Candidate> byFqn, string helperFqn)
    {
        if (ReportUnbindable(spc, c))
        {
            EmitStub(sb, c);
            return;
        }

        // Multi-route: validate EVERY declared template (so RASK004/005/006 fire on any
        // misconfigured template) and aggregate the set of matched RouteParam property names
        // across all of them. A RouteParam that appears in at least one template's segments
        // is considered bound — RASK008 only fires for properties that no template references.
        List<ITemplatePart>? firstParts = null;
        List<ResolvedPathParam>? firstResolved = null;
        var matchedAcrossTemplates = new HashSet<string>(StringComparer.Ordinal);

        for (var i = 0; i < c.Templates.Count; i++)
        {
            if (!TryResolveFullTemplate(spc, c, byFqn, i, out var fullTemplate))
            {
                EmitStub(sb, c);
                return;
            }

            if (!TryParseTemplate(fullTemplate, out var parts, out var error))
            {
                spc.ReportDiagnostic(Diagnostic.Create(Rask003, c.RouteAttrLocation.ToLocation(), fullTemplate,
                    c.FullyQualifiedName, error));
                EmitStub(sb, c);
                return;
            }

            if (ResolvePathParams(spc, c, parts, matchedAcrossTemplates) is not { } resolved)
            {
                EmitStub(sb, c);
                return;
            }

            if (i == 0)
            {
                firstParts = parts;
                firstResolved = resolved;
            }
        }

        // RASK008: orphan [RouteParam] = a property no template segment binds.
        var orphan = c.Properties.FirstOrDefault(p => p.HasRouteParam && !matchedAcrossTemplates.Contains(p.Name));
        if (orphan.Name is not null)
        {
            spc.ReportDiagnostic(Diagnostic.Create(Rask008, orphan.Location.ToLocation(), c.FullyQualifiedName,
                orphan.Name, orphan.RouteParamName ?? orphan.Name));
            EmitStub(sb, c);
            return;
        }

        var queryProps = c.Properties.Where(p => p.HasQueryParam).ToList();

        // URL formatter is built from the first template only — see TryResolveFullTemplate's
        // index-0 comment for the rationale.
        EmitFactoryBody(sb, extSb, c, firstParts!, firstResolved!, queryProps, helperFqn);
    }

    // Binds each path parameter of one template to its [RouteParam] property, or reports why one
    // cannot be bound (RASK004/005/006) and returns null.
    private static List<ResolvedPathParam>? ResolvePathParams(
        SourceProductionContext spc, Candidate c, List<ITemplatePart> parts, HashSet<string> matchedAcrossTemplates)
    {
        var resolved = new List<ResolvedPathParam>();
        foreach (var p in parts.OfType<ParamPart>())
        {
            var prop = c.Properties.FirstOrDefault(x =>
                x.HasRouteParam &&
                string.Equals(x.RouteParamName ?? x.Name, p.Name, StringComparison.OrdinalIgnoreCase));
            if (prop.Name is null)
            {
                spc.ReportDiagnostic(Diagnostic.Create(Rask004, c.RouteAttrLocation.ToLocation(), p.Name,
                    c.FullyQualifiedName));
                return null;
            }

            if (prop.HasQueryParam)
            {
                spc.ReportDiagnostic(Diagnostic.Create(Rask006, prop.Location.ToLocation(), c.FullyQualifiedName,
                    prop.Name, p.Name));
                return null;
            }

            if (!IsTypeCompatible(prop.UnderlyingTypeName, p.Constraint))
            {
                spc.ReportDiagnostic(Diagnostic.Create(Rask005, prop.Location.ToLocation(), c.FullyQualifiedName,
                    prop.Name, prop.TypeFqn, p.Constraint ?? "(none)"));
                return null;
            }

            matchedAcrossTemplates.Add(prop.Name);
            resolved.Add(new ResolvedPathParam(p, prop));
        }

        return resolved;
    }

    // The doc on a generated route helper. `Routes.UserPage(42)` is what a link is SUPPOSED to be written
    // as instead of "/users/42", and the reason is worth stating where it is read: the helper is the only
    // form in which a changed template becomes a compile error rather than a dead link found by a user.
    //
    // Summary only, deliberately — no <param>. CS1573 fires per undocumented parameter as soon as ANY is
    // documented, and these parameters come from the URL template rather than from anything the generator
    // can describe. Documenting none of them keeps a consumer's warnings-as-errors build clean.
    private static void EmitRouteDoc(StringBuilder sb, Candidate c)
    {
        var cref = c.FullyQualifiedName.Replace('<', '{').Replace('>', '}');
        sb.Append("    /// <summary>The URL of <see cref=\"").Append(cref).Append("\"/>");

        if (c.Templates.Count > 0)
        {
            sb.Append(" — <c>").Append(EscapeXml(c.Templates[0])).Append("</c>");
        }

        sb.AppendLine(".</summary>");
        sb.AppendLine("    /// <remarks>");
        sb.AppendLine("    ///     Prefer this over writing the path as a string: a template that changes then breaks the");
        sb.AppendLine("    ///     build here, rather than becoming a link that 404s for whoever clicks it.");
        sb.AppendLine("    /// </remarks>");
    }

    // A route template can legally contain characters that are markup inside a doc comment.
    private static string EscapeXml(string value) =>
        value.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");

    private static void EmitStub(StringBuilder sb, Candidate c)
    {
        sb.Append("    public static ").Append(RouteUrlFullName).Append(' ').Append(c.TypeName).AppendLine("()");
        sb.AppendLine(
            "        => throw new global::System.InvalidOperationException(\"Route source generation failed; see diagnostics.\");");
    }

    private static void EmitFactoryBody(StringBuilder sb, StringBuilder? extSb, Candidate c,
        List<ITemplatePart> parts, List<ResolvedPathParam> pathParams, List<RoutePropInfo> queryProps,
        string helperFqn)
    {
        // Signature: required path params first (in declaration order), then optional, then query
        var orderedPath = pathParams.OrderBy(p => p.Part.Optional ? 1 : 0).ToList();

        EmitRouteDoc(sb, c);
        sb.Append("    public static ").Append(RouteUrlFullName).Append(' ').Append(c.TypeName).Append('(');
        AppendFactoryParameters(sb, orderedPath, queryProps);

        sb.AppendLine(")");
        sb.AppendLine("    {");

        // Build path
        AppendPathExpression(sb, parts, pathParams);

        // Empty path → root
        sb.AppendLine("        if (__path.Length == 0) __path = \"/\";");

        // Build query string
        if (queryProps.Count > 0)
        {
            sb.AppendLine("        global::System.Text.StringBuilder? __qs = null;");
            AppendQueryString(sb, queryProps);

            sb.Append("        return new ").Append(RouteUrlFullName).Append("(__path, __qs?.ToString(), typeof(")
                .Append(c.FullyQualifiedName).AppendLine("));");
        }
        else
        {
            sb.Append("        return new ").Append(RouteUrlFullName).Append("(__path, null, typeof(")
                .Append(c.FullyQualifiedName).AppendLine("));");
        }

        sb.AppendLine("    }");

        if (extSb is not null)
        {
            EmitNavigationExtension(extSb, c, orderedPath, queryProps, helperFqn);
        }
    }

    private static void AppendFactoryParameters(
        StringBuilder sb, List<ResolvedPathParam> orderedPath, List<RoutePropInfo> queryProps)
    {
        var first = true;
        foreach (var rp in orderedPath)
        {
            if (!first)
            {
                sb.Append(", ");
            }

            first = false;
            var paramType = PathParamTypeFqn(rp.Prop, rp.Part.Constraint, rp.Part.Optional);
            sb.Append(paramType).Append(' ').Append(rp.Prop.Name);
            if (rp.Part.Optional)
            {
                sb.Append(" = null");
            }
        }

        foreach (var qp in queryProps)
        {
            if (!first)
            {
                sb.Append(", ");
            }

            first = false;
            var paramType = qp.IsNullable ? qp.TypeFqn : qp.TypeFqn + "?";
            sb.Append(paramType).Append(' ').Append(qp.Name).Append(" = null");
        }
    }

    private static void AppendPathExpression(StringBuilder sb, List<ITemplatePart> parts, List<ResolvedPathParam> pathParams)
    {
        sb.Append("        var __path = \"\"");
        var pendingLiteral = new StringBuilder();
        for (var i = 0; i < parts.Count; i++)
        {
            var part = parts[i];
            if (part is LiteralPart lp)
            {
                pendingLiteral.Append('/').Append(lp.Value);
            }
            else if (part is ParamPart pp)
            {
                var rp = pathParams.First(x => x.Part == pp);
                if (pp.Optional)
                {
                    if (pendingLiteral.Length > 0)
                    {
                        sb.Append(" + \"").Append(EscapeForCSharpStringLiteral(pendingLiteral.ToString())).Append('"');
                        pendingLiteral.Clear();
                    }

                    var ident = rp.Prop.Name;
                    sb.Append(" + (").Append(ident).Append(" is null ? \"\" : \"/\" + ").Append(FormatExpr(ident))
                        .Append(')');
                }
                else
                {
                    pendingLiteral.Append('/');
                    if (pendingLiteral.Length > 0)
                    {
                        sb.Append(" + \"").Append(EscapeForCSharpStringLiteral(pendingLiteral.ToString())).Append('"');
                        pendingLiteral.Clear();
                    }

                    var ident = rp.Prop.Name;
                    sb.Append(" + ").Append(FormatExpr(ident));
                }
            }
        }

        if (pendingLiteral.Length > 0)
        {
            sb.Append(" + \"").Append(EscapeForCSharpStringLiteral(pendingLiteral.ToString())).Append('"');
        }

        sb.AppendLine(";");
    }

    private static void AppendQueryString(StringBuilder sb, List<RoutePropInfo> queryProps)
    {
        foreach (var qp in queryProps)
        {
            var ident = qp.Name;
            var qpName = qp.QueryParamName ?? qp.Name;
            // URL-encode the query KEY at generation time (the value is encoded at runtime via
            // EncodeExpr). The key is a compile-time constant, so baking the encoded form costs
            // nothing and keeps an explicit [QueryParam("a b")] / a name with '&'/'=' from
            // emitting a malformed query string. Property-name-derived keys are valid
            // identifiers, so this is a no-op for them.
            var encodedKey = Uri.EscapeDataString(qpName);
            if (qp.IsNullable)
            {
                sb.Append("        if (").Append(ident).AppendLine(" is not null)");
                sb.AppendLine("        {");
                sb.AppendLine("            __qs ??= new global::System.Text.StringBuilder();");
                sb.AppendLine("            __qs.Append(__qs.Length == 0 ? '?' : '&');");
                sb.Append("            __qs.Append(\"").Append(EscapeForCSharpStringLiteral(encodedKey))
                    .AppendLine("=\");");
                sb.Append("            __qs.Append(").Append(EncodeExpr(ident)).AppendLine(");");
                sb.AppendLine("        }");
            }
            else
            {
                // Non-nullable required query param — always emit
                sb.AppendLine("        __qs ??= new global::System.Text.StringBuilder();");
                sb.AppendLine("        __qs.Append(__qs.Length == 0 ? '?' : '&');");
                sb.Append("        __qs.Append(\"").Append(EscapeForCSharpStringLiteral(encodedKey))
                    .AppendLine("=\");");
                sb.Append("        __qs.Append(").Append(EncodeExpr(ident)).AppendLine(");");
            }
        }
    }

    /// <summary>
    ///     Emits the per-page <c>SomePage.Url(...)</c> / <c>SomePage.Go(...)</c> pair as a C# 14 static
    ///     extension block. Both mirror the legacy <c>Routes.SomePage(...)</c> signature exactly — the same
    ///     path params (required before optional) then the query params — and <c>Url</c> forwards to it, so
    ///     the URL-building logic has exactly one implementation.
    ///     <para>
    ///         The block is emitted into the page's OWN namespace. Extension members resolve only when their
    ///         containing namespace is imported (a fully-qualified <c>My.Ns.SomePage.Go()</c> with no
    ///         <c>using</c> does not compile), so co-locating them means the import that brings the page type
    ///         into scope also brings its navigation helpers.
    ///     </para>
    ///     <para>
    ///         Each page gets its OWN container class. A static extension member lowers to a plain static
    ///         method on the containing class with no receiver parameter, so two pages that both take no
    ///         route parameters would lower to two identical <c>Url()</c> signatures and collide with CS0111
    ///         if they shared one container.
    ///     </para>
    /// </summary>
    private static void EmitNavigationExtension(StringBuilder sb, Candidate c,
        List<ResolvedPathParam> orderedPath, List<RoutePropInfo> queryProps, string helperFqn)
    {
        // Must not be more visible than the page itself: the receiver type is part of a static extension
        // member's signature, so a public container over an internal page is CS0051.
        sb.Append(c.IsPubliclyVisible ? "public" : "internal").Append(" static class __RaskNav_")
            .AppendLine(c.TypeName);
        sb.AppendLine("{");
        sb.Append("    extension(").Append(c.FullyQualifiedName).AppendLine(")");
        sb.AppendLine("    {");

        sb.Append("        public static ").Append(RouteUrlFullName).Append(" Url(");
        AppendSignature(sb, orderedPath, queryProps);
        sb.Append(')').AppendLine();
        sb.Append("            => ").Append(helperFqn).Append('(');
        AppendArguments(sb, orderedPath, queryProps);
        sb.AppendLine(");");
        sb.AppendLine();

        // Go returns the facade's step, so a page replaces the history entry the way a path does:
        // ProductPage.Go(42).Replacing(). Through the public Go facade: this lands in the app's assembly.
        sb.Append("        public static global::Rask.Core.GoTo Go(");
        AppendSignature(sb, orderedPath, queryProps);
        sb.Append(')').AppendLine();
        sb.Append("            => global::Rask.Core.Go.To(").Append(helperFqn).Append('(');
        AppendArguments(sb, orderedPath, queryProps);
        sb.AppendLine("));");

        sb.AppendLine("    }");
        sb.AppendLine("}");
    }

    private static void AppendSignature(StringBuilder sb, List<ResolvedPathParam> orderedPath,
        List<RoutePropInfo> queryProps)
    {
        var first = true;
        foreach (var rp in orderedPath)
        {
            if (!first)
            {
                sb.Append(", ");
            }

            first = false;
            sb.Append(PathParamTypeFqn(rp.Prop, rp.Part.Constraint, rp.Part.Optional)).Append(' ')
                .Append(rp.Prop.Name);
            if (rp.Part.Optional)
            {
                sb.Append(" = null");
            }
        }

        foreach (var qp in queryProps)
        {
            if (!first)
            {
                sb.Append(", ");
            }

            first = false;
            sb.Append(qp.IsNullable ? qp.TypeFqn : qp.TypeFqn + "?").Append(' ').Append(qp.Name).Append(" = null");
        }
    }

    private static void AppendArguments(StringBuilder sb, List<ResolvedPathParam> orderedPath,
        List<RoutePropInfo> queryProps)
    {
        var first = true;
        foreach (var rp in orderedPath)
        {
            if (!first)
            {
                sb.Append(", ");
            }

            first = false;
            sb.Append(rp.Prop.Name);
        }

        foreach (var qp in queryProps)
        {
            if (!first)
            {
                sb.Append(", ");
            }

            first = false;
            sb.Append(qp.Name);
        }
    }

    private static string FormatExpr(string ident)
        => FormatterFullName + ".Format(" + ident + ")";

    private static string EncodeExpr(string ident)
        => FormatterFullName + ".Format(" + ident + ")";

    private static string PathParamTypeFqn(RoutePropInfo prop, string? constraint, bool optional)
    {
        if (constraint is null)
        {
            if (!optional)
            {
                return prop.TypeFqn;
            }

            return prop.IsNullable ? prop.TypeFqn : prop.TypeFqn + "?";
        }

        switch (constraint)
        {
            case "int": return optional ? "int?" : "int";
            case "long": return optional ? "long?" : "long";
            case "bool": return optional ? "bool?" : "bool";
            case "guid": return optional ? "global::System.Guid?" : "global::System.Guid";
            default: return optional ? "string?" : "string";
        }
    }

    private static bool IsTypeCompatible(string underlyingTypeName, string? constraint)
    {
        if (constraint is null)
        {
            return true;
        }

        switch (constraint)
        {
            case "int": return string.Equals(underlyingTypeName, "int", StringComparison.Ordinal);
            case "long": return string.Equals(underlyingTypeName, "long", StringComparison.Ordinal);
            case "bool": return string.Equals(underlyingTypeName, "bool", StringComparison.Ordinal);
            case "guid": return string.Equals(underlyingTypeName, "global::System.Guid", StringComparison.Ordinal);
            default: return string.Equals(underlyingTypeName, "string", StringComparison.Ordinal);
        }
    }
}
