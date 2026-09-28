using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Rask.Generators.ScopedScripts;

/// <summary>One component's generated members, collected export by export and then wrapped in its partials.</summary>
/// <param name="typeName">The component's own name.</param>
/// <param name="ns">Its namespace, or empty for the global one.</param>
/// <param name="containers">The types it is nested in, outermost first, one declaration header per line.</param>
/// <param name="memberNames">Every member name the component's own code declares, one per line.</param>
/// <param name="fileName">The script's file name, for the generated summaries.</param>
/// <param name="reserved">The framework's reachable member names, which a generated member hides with <c>new</c>.</param>
/// <param name="report">Reports an export that gets no member, with the reason (RASK094).</param>
internal sealed class ScriptEmitter(
    string typeName,
    string ns,
    string containers,
    string memberNames,
    string fileName,
    HashSet<string> reserved,
    Action<string, string> report)
{
    private const string Runtime = "global::Rask.Core.ScopedAssets.ScopedScript";
    private const string ValueTask = "global::System.Threading.Tasks.ValueTask";

    private readonly StringBuilder _body = new();
    private readonly StringBuilder _proxies = new();
    private readonly string _identifier = "Rask." + typeName + ".";

    // Two kinds of name a generated member can meet. One the component's own code declares is a real
    // clash and is reported. One it only INHERITS — a markup entry such as SVG's `Stop`, a framework
    // member — is hidden with `new`, the way a component's own member hides one today (`Markup.Stop`
    // still reaches the tag); otherwise a script could not export `stop()` or `filter()` at all.
    private readonly HashSet<string> _taken = new(memberNames.Split('\n'), StringComparer.Ordinal);
    private readonly Dictionary<string, string> _recordNames = new(StringComparer.Ordinal);

    private string Access(string name) => reserved.Contains(name) ? "private new" : "private";

    // An interface's record takes its name before any function does: `interface Viewport` and
    // `function viewport()` are one C# name, and the record is what a signature refers to.
    public void ClaimRecordNames(TsDeclarations decls)
    {
        foreach (var aliasName in decls.Aliases.Where(a => a.Value is TsObject).Select(a => a.Key).ToList())
        {
            var pascal = Names.Pascal(aliasName);
            if (pascal is null || _taken.Contains(pascal))
            {
                decls.Aliases[aliasName] = new TsUnsupported(pascal is null
                    ? $"'{aliasName}', whose name is not a C# identifier"
                    : $"'{aliasName}', whose C# name '{pascal}' {typeName} already uses");
                continue;
            }

            _recordNames[pascal] = aliasName;
        }
    }

    public void EmitClass(TsClassDecl cls, TypeMapper mapper)
    {
        if (cls.Generic)
        {
            report(cls.Name, "a generic class has no single C# shape");
            mapper.Classes.Remove(cls.Name);
            return;
        }

        var csName = Names.Pascal(cls.Name);
        if (ClassNameClash(csName) is { } clash)
        {
            report(cls.Name, clash);
            mapper.Classes.Remove(cls.Name);
            return;
        }

        _proxies.Append(EmitProxy(cls, csName!, Access(csName!), mapper, report));

        if (cls.Constructors.Count == 1)
        {
            EmitConstructor(cls, csName!, mapper);
        }
        else if (cls.Constructors.Count > 1)
        {
            report(cls.Name, "its constructor is overloaded, so there is no one New method to write — keep one signature");
        }
    }

    private string? ClassNameClash(string? csName)
    {
        if (csName is null)
        {
            return "its name is not a C# identifier";
        }

        if (_recordNames.TryGetValue(csName, out var shape))
        {
            return $"the interface '{shape}' already takes the C# name '{csName}' — rename one of them";
        }

        return _taken.Add(csName) ? null : $"{typeName} already has a member named '{csName}' — rename one of them";
    }

    private void EmitConstructor(TsClassDecl cls, string csName, TypeMapper mapper)
    {
        var newName = "New" + csName;
        var ctor = new TsFunctionDecl(newName, cls.Constructors[0], new TsNamed(cls.Name), false, cls.Doc);
        if (!_taken.Add(newName))
        {
            report(cls.Name, $"{typeName} already has a member named '{newName}'");
            return;
        }

        var method = EmitMethod(ctor, newName, _identifier + "__new_" + cls.Name, mapper, Owner.Component,
            $"Creates a <c>{cls.Name}</c> from <c>{fileName}</c>.", out var error,
            accessibility: Access(newName));
        if (method is null)
        {
            report(cls.Name, error!);
        }
        else
        {
            _body.Append(method);
        }
    }

    public void EmitFunction(TsFunctionDecl fn, TypeMapper mapper)
    {
        var csName = Names.Pascal(fn.Name);
        if (csName is null)
        {
            report(fn.Name, "its name is not a C# identifier");
            return;
        }

        if (_recordNames.TryGetValue(csName, out var shape))
        {
            report(fn.Name, $"the interface '{shape}' already takes the C# name '{csName}' — rename the function (say, 'read{csName}')");
            return;
        }

        if (!_taken.Add(csName))
        {
            report(fn.Name, $"{typeName} already has a member named '{csName}' — rename one of them");
            return;
        }

        var method = EmitMethod(fn, csName, _identifier + fn.Name, mapper, Owner.Component,
            $"Calls <c>{fn.Name}</c> in <c>{fileName}</c>.", out var error, accessibility: Access(csName));
        if (method is null)
        {
            report(fn.Name, error!);
            return;
        }

        _body.Append(method);
    }

    public string? Assemble(TypeMapper mapper)
    {
        var records = new StringBuilder();
        foreach (var record in mapper.Records)
        {
            records.Append(EmitRecord(record, Access(record.CsName)));
        }

        if (_body.Length == 0 && _proxies.Length == 0)
        {
            return null;
        }

        var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated />");
        sb.AppendLine("#nullable enable");
        sb.AppendLine("#pragma warning disable CS1591");
        sb.AppendLine();
        if (ns.Length > 0)
        {
            sb.Append("namespace ").Append(ns).AppendLine(";");
            sb.AppendLine();
        }

        var partials = containers.Length == 0 ? Array.Empty<string>() : containers.Split('\n');
        foreach (var container in partials)
        {
            sb.Append("partial ").AppendLine(container).AppendLine("{");
        }

        sb.Append("partial class ").AppendLine(typeName).AppendLine("{");
        sb.Append(_body);
        sb.Append(_proxies);
        sb.Append(records);
        sb.AppendLine("}");
        foreach (var _ in partials)
        {
            sb.AppendLine("}");
        }

        return sb.ToString();
    }


    private enum Owner
    {
        Component,
        Proxy,
    }

    private static string EmitProxy(
        TsClassDecl cls, string csName, string accessibility, TypeMapper mapper, Action<string, string> report)
    {
        var sb = new StringBuilder();
        AppendSummary(sb, "    ", cls.Doc, $"The <c>{cls.Name}</c> class of this component's script. Released when the component unmounts.");
        sb.Append("    ").Append(accessibility).Append(" sealed class ").Append(csName).AppendLine(" : global::Rask.Core.ScopedAssets.ScriptObject");
        sb.AppendLine("    {");
        sb.Append("        internal ").Append(csName)
            .AppendLine("(global::Microsoft.JSInterop.IJSObjectReference reference) : base(reference) { }");

        var taken = new HashSet<string>(StringComparer.Ordinal)
        {
            csName, "Reference", "DisposeAsync", "CallScript", "CallScriptObject", "ScriptCallback",
            "Equals", "GetHashCode", "GetType", "ToString", "MemberwiseClone", "Finalize",
        };
        foreach (var group in cls.Methods.GroupBy(m => m.Name, StringComparer.Ordinal))
        {
            var method = group.First();
            var member = cls.Name + "." + method.Name;
            if (group.Count() > 1)
            {
                report(member, "it is overloaded — keep one signature");
                continue;
            }

            var name = Names.Pascal(method.Name);
            if (name is null || !taken.Add(name))
            {
                report(member, name is null ? "its name is not a C# identifier" : $"'{name}' is already taken on the proxy");
                continue;
            }

            var text = EmitMethod(method, name, method.Name, mapper, Owner.Proxy,
                $"Calls <c>{method.Name}</c> on this instance.", out var error, "        ", "public");
            if (text is null)
            {
                report(member, error!);
                continue;
            }

            sb.Append(text);
        }

        sb.AppendLine("    }");
        sb.AppendLine();
        return sb.ToString();
    }

    private static string? EmitMethod(
        TsFunctionDecl fn,
        string csName,
        string identifier,
        TypeMapper mapper,
        Owner owner,
        string fallbackSummary,
        out string? error,
        string indent = "    ",
        string accessibility = "private")
    {
        if (fn.Generic)
        {
            error = "a generic function has no single C# signature";
            return null;
        }

        var parameters = new List<(MappedParameter Mapped, string Name)>();
        var records = new List<string>();
        error = MapParameters(fn, mapper, owner, parameters, records);
        if (error is not null)
        {
            return null;
        }

        var ret = mapper.MapReturn(fn.Returns);
        if (ret.Error is not null)
        {
            error = $"it returns {ret.Error}";
            return null;
        }

        records.AddRange(ret.Records);

        var header = MethodHeader(fn, indent, fallbackSummary, records);
        var args = parameters.Select(p => string.Format(CultureInfo.InvariantCulture, p.Mapped.ArgFormat!, p.Name)).ToList();
        var argArray = args.Count == 0
            ? "global::System.Array.Empty<object?>()"
            : "new object?[] { " + string.Join(", ", args) + " }";

        // An unset optional argument must reach the script as `undefined`, which JSON cannot carry - so the
        // trailing ones are dropped instead of sent as null, and the script's own defaults apply.
        var required = fn.Parameters.TakeWhile(p => !p.Optional).Count();
        if (required < fn.Parameters.Count)
        {
            argArray = $"{Runtime}.Trim({argArray}, {required})";
        }

        var returnType = ret.Kind == ReturnKind.Void ? ValueTask : ValueTask + "<" + ret.Type + ">";
        var call = CallExpression(owner, ret, identifier, argArray);
        return Overloads(header, parameters, indent, accessibility + " " + returnType + " " + csName, call);
    }

    // Each parameter mapped, in order; the error for the first that cannot cross, or null.
    private static string? MapParameters(
        TsFunctionDecl fn,
        TypeMapper mapper,
        Owner owner,
        List<(MappedParameter Mapped, string Name)> parameters,
        List<string> records)
    {
        foreach (var p in fn.Parameters)
        {
            var mapped = mapper.MapParameter(p, owner == Owner.Proxy);
            if (mapped.Error is not null)
            {
                return $"parameter '{p.Name}' is {mapped.Error}";
            }

            parameters.Add((mapped, Names.Parameter(p.Name)));
            records.AddRange(mapped.Records);
        }

        return null;
    }

    // The doc comment and trimming attributes every overload of one method repeats.
    private static string MethodHeader(TsFunctionDecl fn, string indent, string fallbackSummary, List<string> records)
    {
        var sb = new StringBuilder();
        var (summary, paramDocs, returnsDoc) = DocComment.Split(fn.Doc);
        sb.Append(indent).Append("/// <summary>").Append(Xml(summary.Length > 0 ? summary : null) ?? fallbackSummary).AppendLine("</summary>");
        foreach (var name in fn.Parameters.Select(p => p.Name))
        {
            if (paramDocs.TryGetValue(name, out var text) && text.Length > 0)
            {
                sb.Append(indent).Append("/// <param name=\"").Append(Names.Parameter(name).TrimStart('@')).Append("\">")
                    .Append(Xml(text)).AppendLine("</param>");
            }
        }

        if (returnsDoc is { Length: > 0 })
        {
            sb.Append(indent).Append("/// <returns>").Append(Xml(returnsDoc)).AppendLine("</returns>");
        }

        // Keep a record's members through trimming: it crosses to the script by reflection over its
        // runtime type, which nothing else in a trimmed app roots.
        foreach (var record in records.Distinct(StringComparer.Ordinal))
        {
            sb.Append(indent)
                .Append("[global::System.Diagnostics.CodeAnalysis.DynamicDependency(global::System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicProperties | global::System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicConstructors, typeof(")
                .Append(record).AppendLine("))]");
        }

        return sb.ToString();
    }

    private static string CallExpression(Owner owner, MappedReturn ret, string identifier, string argArray)
    {
        // A tuple arrives as a JSON array; the generated reader takes it apart with each element's own type.
        var readTuple = ret.Kind == ReturnKind.Tuple
            ? $"static __r => ({string.Join(", ", ret.Elements.Select((t, i) => $"{Runtime}.Item<{t}>(__r, {i})"))})"
            : null;

        if (owner == Owner.Component)
        {
            return ret.Kind switch
            {
                ReturnKind.Tuple => $"{Runtime}.Tuple<{ret.Type}>(this, \"{identifier}\", {readTuple}, {argArray})",
                ReturnKind.Void => $"{Runtime}.Call(this, \"{identifier}\", {argArray})",
                ReturnKind.Object => $"{Runtime}.NewObject(this, \"{identifier}\", static __r => new {ret.Type}(__r), {argArray})",
                _ => $"{Runtime}.Call<{ret.Type}>(this, \"{identifier}\", {argArray})",
            };
        }

        return ret.Kind switch
        {
            ReturnKind.Tuple => $"CallScriptTuple<{ret.Type}>(\"{identifier}\", {readTuple}, {argArray})",
            ReturnKind.Void => $"CallScript(\"{identifier}\", {argArray})",
            ReturnKind.Object => $"CallScriptObject(\"{identifier}\", static __r => new {ret.Type}(__r), {argArray})",
            _ => $"CallScript<{ret.Type}>(\"{identifier}\", {argArray})",
        };
    }

    // One overload per sync/async choice of each callback parameter — see TypeMapper.MapCallback.
    private static string Overloads(
        string header, List<(MappedParameter Mapped, string Name)> parameters, string indent, string signature, string call)
    {
        var sb = new StringBuilder();
        var callbacks = parameters.Select((p, i) => (p, i)).Where(x => x.p.Mapped.AsyncType is not null)
            .Select(x => x.i).ToList();
        for (var mask = 0; mask < 1 << callbacks.Count; mask++)
        {
            var list = new string[parameters.Count];
            var defaulted = true;
            for (var i = parameters.Count - 1; i >= 0; i--)
            {
                var (mapped, name) = parameters[i];
                var slot = callbacks.IndexOf(i);
                var type = slot >= 0 && (mask & (1 << slot)) != 0 ? mapped.AsyncType : mapped.Type;

                // Only the all-sync overload keeps a callback's default, so a call that omits it binds to
                // exactly one; and a default is dropped from anything left of a parameter that has none.
                defaulted &= mapped.Optional && (slot < 0 || mask == 0);
                list[i] = type + " " + name + (defaulted ? mapped.DefaultValue : string.Empty);
            }

            sb.Append(header);
            sb.Append(indent).Append(signature).Append('(').Append(string.Join(", ", list)).AppendLine(") =>");
            sb.Append(indent).Append("    ").Append(call).AppendLine(";");
            sb.AppendLine();
        }

        return sb.ToString();
    }

    private static string EmitRecord(RecordShape record, string accessibility)
    {
        var sb = new StringBuilder();
        AppendSummary(sb, "    ", record.Doc, $"The <c>{record.TsName}</c> shape of this component's script.");
        sb.Append("    ").Append(accessibility).Append(" sealed record ").AppendLine(record.CsName);
        sb.AppendLine("    {");
        foreach (var p in record.Properties)
        {
            sb.Append("        [global::System.Text.Json.Serialization.JsonPropertyName(\"").Append(p.JsonName).AppendLine("\")]");
            if (!p.Required)
            {
                // Left out when unset, so the script reads `undefined` for it as it would for its own optional field.
                sb.AppendLine("        [global::System.Text.Json.Serialization.JsonIgnore(Condition = global::System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]");
            }

            sb.Append("        public ").Append(p.Required ? "required " : string.Empty).Append(p.Type).Append(' ')
                .Append(p.CsName).AppendLine(" { get; init; }");
            sb.AppendLine();
        }

        sb.AppendLine("    }");
        sb.AppendLine();
        return sb.ToString();
    }

    private static void AppendSummary(StringBuilder sb, string indent, string? doc, string fallback)
    {
        var summary = DocComment.Split(doc).Summary;
        sb.Append(indent).Append("/// <summary>").Append(summary.Length > 0 ? Xml(summary) : fallback).AppendLine("</summary>");
    }

    private static readonly Regex CodeSpan = new("`(?<code>[^`]+)`", RegexOptions.None, TimeSpan.FromSeconds(1));

    // JSDoc is Markdown-ish: `code` spans become <c>, which is what an IDE renders as code.
    private static string? Xml(string? text) =>
        text is null
            ? null
            : CodeSpan.Replace(
                text.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\r", string.Empty).Replace("\n", " "),
                "<c>${code}</c>");
}
