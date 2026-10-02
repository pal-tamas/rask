using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Rask.Generators.Json;

namespace Rask.Core.Dom.Build;

// One generated member of a Rask.Web proxy, written as an instance member over the proxy's chain and, for a global, as
// a static that forwards to the global's instance.
//   an attribute holding a live object → a proxy one step further along the chain (no round trip)
//   an attribute holding a value       → an awaitable read, and Set{Name} where it is writable
//   an operation returning a value     → a method that runs the chain (Promise<T> awaited)
//   an operation returning an object   → a proxy over the call; kept (a handle) when the object comes by Promise
//   a static member                    → the same, on the class, from window.{Interface}
//   a constructor                      → static Create(…), the new object kept
internal sealed class WebMember
{
    private const string TypesNs = "global::Rask.Web.Types.";

    private readonly JsonNode _data;
    private readonly string _summary;
    private readonly string _returns;
    private readonly string? _parameters;
    private readonly string _arguments;
    private readonly string _body;

    // "" for an instance member, "static " or "static new " (hiding a base's Create of the same parameters).
    private readonly string _modifiers;

    private WebMember(
        JsonNode data, string summary, string returns, string name, string? parameters, string arguments, string body, string modifiers = "",
        string parameterTypes = "")
    {
        _data = data;
        _summary = summary;
        _returns = returns;
        Name = name;
        _parameters = parameters;
        _arguments = arguments;
        _body = body;
        _modifiers = modifiers;
        ParameterTypes = parameterTypes;
    }

    public string Name { get; }

    // Its type parameters: one per IDL `any` it takes (TMessage), and T for an `any` it answers with.
    private string[] Generics { get; set; } = Array.Empty<string>();

    // What JSInterop asks of a type it reads or writes, so the trimmer keeps an app's type whole.
    internal const string JsonMembers =
        "[global::System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembers(" +
        "global::System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicConstructors | " +
        "global::System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicFields | " +
        "global::System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicProperties)] ";

    private const string AnyArg = "global::Rask.Web.JsChain.Any(";

    private static readonly string[] ReadsT = { "T" };
    private static readonly string[] WritesTValue = { "TValue" };

    // Members that write into the bytes they are handed and answer with something else: the bytes cross as a copy, so
    // C# would never see them filled, and their overloads that take bytes are not written. (getRandomValues answers with
    // the filled array, so it is not here.)
    private static readonly HashSet<string> Fills = new(StringComparer.Ordinal)
    {
        "AnalyserNode.getByteFrequencyData", "AnalyserNode.getByteTimeDomainData", "AudioData.copyTo", "EncodedAudioChunk.copyTo",
        "EncodedVideoChunk.copyTo", "VideoFrame.copyTo", "TextEncoder.encodeInto", "WebGLRenderingContext.readPixels",
        "WebGL2RenderingContext.readPixels", "WebGL2RenderingContext.getBufferSubData",
    };

    private string TypeParameters => Generics.Length == 0 ? "" : "<" + string.Join(", ", Generics.Select(g => JsonMembers + g)) + ">";

    private string TypeArguments => Generics.Length == 0 ? "" : "<" + string.Join(", ", Generics) + ">";

    // The type parameters a combination of arguments brings: each `any` one's.
    private static string[] GenericsOf(List<(string Type, string Name, string Arg)> parameters) =>
        parameters.Where(p => p.Arg.StartsWith(AnyArg, StringComparison.Ordinal)).Select(p => p.Type).ToArray();

    // Whether only WebAssembly can run it (WebHost): Rask.Wasm declares it, as an extension member, and Rask.Web does not.
    public bool Wasm { get; private set; }

    // The parameter types alone: how a derived Create is told apart from, or hides, a base's.
    public string ParameterTypes { get; }

    // Name and parameter types: what makes two members the same overload.
    public string Signature => Name + "(" + (_parameters ?? "") + ")";

    // The static members: the class's own, reached from window.{Interface} (URL.CanParse, Notification.Permission).
    public static List<WebMember> StaticsOf(string iface, JsonNode data, DomValueTypes types, HashSet<string> proxies, HashSet<string> taken)
    {
        var root = $"global::Rask.Web.JsChain.Window.Get(\"{iface}\")";
        return Of(iface, Holding(data["statics"]?.Items ?? new List<JsonNode>()), types, proxies, taken, new HashSet<string>(StringComparer.Ordinal))
            .Select(m => m.AsStatic(root)).ToList();
    }

    // An interface's data holding only these members, for Of to write: its statics, or a subset of its members.
    public static JsonNode Holding(IEnumerable<JsonNode> members)
    {
        var list = new JsonNode(JsonKind.Array, 0, 0);
        list.Items.AddRange(members);
        var data = new JsonNode(JsonKind.Object, 0, 0);
        data.Members.Add(new KeyValuePair<string, JsonNode>("members", list));
        return data;
    }

    // `new X(…)` as X.Create(…): the new object, kept in the browser. `hidden` holds the parameter types of a base's
    // Create this one hides (EventTarget has a constructor too).
    public static List<WebMember> ConstructorsOf(
        string iface, JsonNode data, DomValueTypes types, HashSet<string> proxies, HashSet<string> hidden, JsonNode? callbacks)
    {
        var result = new List<WebMember>();
        var proxy = TypesNs + iface;
        var signatures = new HashSet<string>(StringComparer.Ordinal);
        foreach (var ctor in data["constructors"]?.Items ?? new List<JsonNode>())
        {
            var args = ctor["args"]?.Items ?? new List<JsonNode>();
            if (args.Any(x => x["variadic"]?.AsBoolean() == true))
            {
                continue;
            }

            var distinct = DomRefEmitter.Prefixes(args).SelectMany(prefix => Expand(prefix, types, callbacks, proxies))
                .Where(p => signatures.Add(string.Join(",", p.Select(x => x.Type))));
            foreach (var parameters in distinct)
            {
                var typesOnly = string.Join(",", parameters.Select(p => p.Type));
                var names = string.Join(", ", parameters.Select(p => p.Name));
                var array = parameters.Count == 0 ? "" : ", new object?[] { " + string.Join(", ", parameters.Select(p => p.Arg)) + " }";
                result.Add(new WebMember(ctor, $"MDN's <c>new {iface}()</c>: the new object, kept in the browser until you dispose of it.",
                    $"ValueTask<{proxy}>", "Create", string.Join(", ", parameters.Select(p => p.Type + " " + p.Name)), names,
                    $"global::Rask.Web.JsChain.Window.New(\"{iface}\"{array}).Keep(static c => new {proxy}(c))",
                    hidden.Contains(typesOnly) ? "static new " : "static ", typesOnly)
                { Generics = GenericsOf(parameters), Wasm = WebHost.Mentions(typesOnly) });
            }
        }

        return result;
    }

    // Every combination of the arguments' types that crosses the wire, each with how it goes into the call: a value
    // as itself, an element as its ElementRef, a live object as one you kept (`proxies`), which crosses as its handle,
    // a callback as a C# handler handed over by JsChain's Callback, or Awaited for one whose result the browser waits on. A method with one callback takes it sync or async; with more, each is
    // an Action. An `any` is the caller's own type, TMessage, which JsChain's Any hands the host's runtime to write; an
    // `object` is the dictionary WebObjectArgs names for it (`owner` is Interface.member), else nothing.
    internal static IEnumerable<List<(string Type, string Name, string Arg)>> Expand(
        List<JsonNode> args, DomValueTypes types, JsonNode? callbacks, HashSet<string> proxies, string? owner = null)
    {
        IEnumerable<List<(string Type, string Name, string Arg)>> combos = new[] { new List<(string Type, string Name, string Arg)>() };
        foreach (var a in args)
        {
            var idlName = a["name"]!.AsString()!;
            var name = DomRefEmitter.Identifier(idlName);
            var alternatives = new List<(string Type, string Arg)>();
            foreach (var t in DomValueTypes.Alternatives(a["type"]!.AsString()!))
            {
                if (types.CSharp(t, returned: false) is { } value)
                {
                    alternatives.Add((value, name));
                }
                else if (string.Equals(t, "any", StringComparison.Ordinal))
                {
                    alternatives.Add(("T" + DomRefEmitter.Pascal(idlName), AnyArg + name + ")"));
                }
                else if (string.Equals(t.TrimEnd('?'), "object", StringComparison.Ordinal) && owner is not null && WebObjectArgs.Dictionaries.TryGetValue(owner + "." + idlName, out var dictionary))
                {
                    alternatives.Add((types.CSharp(dictionary + (t.EndsWith("?", StringComparison.Ordinal) ? "?" : ""), returned: false)
                                      ?? throw new DomEmitException($"WebObjectArgs names {dictionary} for {owner}.{idlName}, which the snapshot does not have."), name));
                }
                else if (string.Equals(t.TrimEnd('?'), "object", StringComparison.Ordinal) && owner is not null && WebObjectArgs.Shapes.TryGetValue(owner + "." + idlName, out var shape))
                {
                    alternatives.Add((types.Serializes(shape) + (t.EndsWith("?", StringComparison.Ordinal) ? "?" : ""), name));
                }
                else if (types.IsElement(t))
                {
                    alternatives.Add((ElementRef + (t.EndsWith("?", StringComparison.Ordinal) ? "?" : ""), name));
                }
                else if (proxies.Contains(t.TrimEnd('?')))
                {
                    alternatives.Add((TypesNs + t, name));
                }
                else
                {
                    alternatives.AddRange(Handlers(t, types, callbacks).Select(h => (h.Type, $"global::Rask.Web.JsChain.{h.Wrap}({name})")));
                }
            }

            // Each type once: BufferSource's views are all a byte[].
            var distinct = alternatives.GroupBy(t => t.Type, StringComparer.Ordinal).Select(g => g.First()).ToList();
            combos = combos.SelectMany(c => distinct.Select(t => new List<(string Type, string Name, string Arg)>(c) { (t.Type, name, t.Arg) })).ToList();
        }

        return combos.Where(c => !c.Any(p => p.Type.StartsWith("global::System.Func<", StringComparison.Ordinal)) || c.Count(p => IsHandler(p.Arg)) == 1);
    }

    private const string ElementRef = "global::Rask.Core.ElementRef";

    private static bool IsHandler(string arg) =>
        arg.StartsWith("global::Rask.Web.JsChain.Callback(", StringComparison.Ordinal) || arg.StartsWith("global::Rask.Web.JsChain.Awaited(", StringComparison.Ordinal);

    // The C# handler types an IDL callback can be written as, Action<…> and Func<…, Task>, taking the leading arguments
    // that cross the wire (an observer's entries, not the observer, which the caller already holds). One the browser
    // waits on — a lock's, held until it returns — is Awaited; one whose result it reads (a predicate) is not written.
    private static IEnumerable<(string Type, string Wrap)> Handlers(string idl, DomValueTypes types, JsonNode? callbacks)
    {
        var nullable = idl.EndsWith("?", StringComparison.Ordinal) ? "?" : "";
        if (callbacks?[idl.TrimEnd('?')] is not { } callback || Wrapper(callback["returns"]?.AsString()) is not { } wrap)
        {
            yield break;
        }

        var mapped = (callback["args"]?.Items ?? new List<JsonNode>())
            .TakeWhile(x => x["variadic"]?.AsBoolean() != true)
            .Select(x => types.CSharp(x["type"]!.AsString()!, returned: false))
            .TakeWhile(x => x is not null).Take(3).Select(x => x!).ToList();
        yield return (mapped.Count == 0 ? "global::System.Action" + nullable : $"global::System.Action<{string.Join(", ", mapped)}>{nullable}", wrap);
        if (mapped.Count <= 1)
        {
            yield return (mapped.Count == 0
                ? "global::System.Func<global::System.Threading.Tasks.Task>" + nullable
                : $"global::System.Func<{mapped[0]}, global::System.Threading.Tasks.Task>{nullable}", wrap);
        }
    }

    // Callback for a callback that returns nothing; Awaited for one whose promise the browser waits on. An `any` result
    // may be read (Observable.map's mapper), so it is not written.
    private static string? Wrapper(string? returns) => returns switch
    {
        "undefined" => "Callback",
        "Promise<undefined>" or "Promise<any>" => "Awaited",
        _ => null,
    };

    // An interface's events, each as On{Event}(handler): sync or async, with the event or without it. The subscription
    // they return removes the listener when disposed of; so does the handler's component unmounting.
    public static List<WebMember> EventsOf(JsonNode data, WebPayloads payloads, HashSet<string> taken)
    {
        var result = new List<WebMember>();
        foreach (var e in data["events"]?.Items ?? new List<JsonNode>())
        {
            var type = e["type"]!.AsString()!;
            var name = DomEventEmitter.HandlerName(type);
            if (!taken.Add(name))
            {
                continue;
            }

            var (payload, fields) = payloads.Of(e["interface"]?.AsString() ?? "Event");
            var head = $"Chain.Listen(\"{type}\", {fields}, handler, static p => new {payload}(p), ";
            var summary = $"MDN's <c>{type}</c> event: the handler runs, in its component's order, each time it fires. Dispose of the subscription to stop.";
            const string returns = "ValueTask<global::System.IAsyncDisposable>";
            result.Add(new WebMember(e, summary, returns, name, $"global::System.Action<{payload}> handler", "handler",
                head + "e => { handler(e); return global::System.Threading.Tasks.Task.CompletedTask; })"));
            result.Add(new WebMember(e, summary, returns, name, $"global::System.Func<{payload}, global::System.Threading.Tasks.Task> handler", "handler",
                head + "handler)"));
            result.Add(new WebMember(e, summary, returns, name, "global::System.Action handler", "handler",
                head + "_ => { handler(); return global::System.Threading.Tasks.Task.CompletedTask; })"));
            result.Add(new WebMember(e, summary, returns, name, "global::System.Func<global::System.Threading.Tasks.Task> handler", "handler",
                head + "_ => handler())"));
        }

        return result;
    }

    private WebMember AsStatic(string root) =>
        new(_data, _summary, _returns, Name, _parameters, _arguments, Rechain(root + "."), "static ", ParameterTypes) { Wasm = Wasm, Generics = Generics };

    // The body over another chain: the member's own `Chain.`, never the JsChain.Callback(…) inside its arguments.
    private string Rechain(string replacement) =>
        Regex.Replace(_body, @"(?<![\w.:])Chain\.", replacement, RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    public static List<WebMember> Of(
        string iface, JsonNode data, DomValueTypes types, HashSet<string> proxies, HashSet<string> taken, HashSet<string> denied, JsonNode? callbacks = null)
    {
        var result = new List<WebMember>();
        foreach (var m in data["members"]?.Items ?? new List<JsonNode>())
        {
            var idl = m["name"]!.AsString()!;
            // CSS's dashed twins (font-display beside fontDisplay) are not identifiers, and the camel-cased one is there.
            if (denied.Contains(iface + "." + idl) || !idl.All(c => char.IsLetterOrDigit(c) || c == '_'))
            {
                continue;
            }

            var added = string.Equals(m["kind"]?.AsString(), "operation", StringComparison.Ordinal)
                ? Operation(m, iface + "." + idl, types, proxies, callbacks)
                : Attribute(m, idl, types, proxies, denied.Contains(iface + "." + idl + "="));
            foreach (var x in added)
            {
                x.Wasm = WebHost.IsWasmMember(iface, idl) || WebHost.Mentions(x._returns + " " + x._parameters);
            }

            result.AddRange(added.Where(x => !taken.Contains(x.Name)));
            taken.UnionWith(added.Select(x => x.Name));
        }

        return result;
    }

    private static List<WebMember> Attribute(JsonNode m, string idl, DomValueTypes types, HashSet<string> proxies, bool readOnly)
    {
        var result = new List<WebMember>();
        var idlType = m["type"]!.AsString()!;
        var prop = DomRefEmitter.Pascal(idl);
        if (ElementRead(m, idl, prop, types) is { } element)
        {
            result.Add(element);
            return result;
        }

        if (LiveOf(idlType, proxies) is { } live)
        {
            result.Add(new WebMember(m, $"MDN's <c>{idl}</c>: the object, one step further along the chain.", TypesNs + live, prop, null, "",
                $"new(Chain.Get(\"{idl}\"))"));

            // Written with an object you kept (a new MediaMetadata), which crosses as its handle; null where MDN allows it.
            if (!readOnly && m["readonly"]?.AsBoolean() != true)
            {
                var nullable = idlType.EndsWith("?", StringComparison.Ordinal) ? "?" : "";
                result.Add(new WebMember(m, $"Sets MDN's <c>{idl}</c> in the browser, to an object you kept.", "ValueTask", "Set" + prop,
                    TypesNs + live + nullable + " value", "value", $"Chain.Write(\"{idl}\", value)"));
            }

            return result;
        }

        // An `any` is read as the caller's own type, and a property cannot ask for one: `await History.State<Cart>()`.
        if (string.Equals(idlType, "any", StringComparison.Ordinal))
        {
            result.Add(new WebMember(m, $"MDN's <c>{idl}</c>, read from the browser as your own type.", "ValueTask<T>", prop, "", "",
                $"Chain.ReadAny<T>(\"{idl}\")")
            { Generics = ReadsT });
            if (!readOnly && m["readonly"]?.AsBoolean() != true)
            {
                result.Add(new WebMember(m, $"Sets MDN's <c>{idl}</c> in the browser.", "ValueTask", "Set" + prop, "TValue value", "value",
                    $"Chain.Write(\"{idl}\", {AnyArg}value))")
                { Generics = WritesTValue });
            }

            return result;
        }

        if (types.CSharp(idlType, returned: true) is not { } value)
        {
            return result;
        }

        // A Promise<undefined> attribute (a view transition's `finished`) is something to wait for, not a value.
        result.Add(string.Equals(value, "void", StringComparison.Ordinal)
            ? new WebMember(m, $"MDN's <c>{idl}</c>: completes when it settles in the browser.", "ValueTask", prop, null, "", $"Chain.Settle(\"{idl}\")")
            : new WebMember(m, $"MDN's <c>{idl}</c>, read from the browser.", $"ValueTask<{value}>", prop, null, "", $"Chain.Read<{value}>(\"{idl}\")"));
        if (!readOnly && m["readonly"]?.AsBoolean() != true && types.CSharp(idlType, returned: false) is { } arg)
        {
            result.Add(new WebMember(m, $"Sets MDN's <c>{idl}</c> in the browser.", "ValueTask", "Set" + prop, arg + " value", "value", $"Chain.Write(\"{idl}\", value)"));
        }

        return result;
    }

    // An element the browser names (document.fullscreenElement) cannot cross, but whether it is one of yours can: it
    // reads as an ElementRef equal to your ref to it, else null. `await Document.FullscreenElement == _stage`.
    private static WebMember? ElementRead(JsonNode m, string idl, string prop, DomValueTypes types)
    {
        // A Node (a selection's anchor) is a text node as often as not, which no ref names.
        var idlType = m["type"]!.AsString()!;
        if (!idlType.EndsWith("?", StringComparison.Ordinal) || !types.IsElement(idlType) || string.Equals(idlType, "Node?", StringComparison.Ordinal))
        {
            return null;
        }

        var element = types.ElementRead();
        return new WebMember(m, $"MDN's <c>{idl}</c>: your ref to the element, equal to the one you rendered it with; null for none, or one you gave no ref.",
            $"ValueTask<{element}>", prop, null, "", $"Chain.Read<{element}>(\"{idl}\")");
    }

    // The live object an attribute holds: its type, or for a union of live objects the first MDN lists, as a call's
    // answer is — a media element's srcObject is a MediaStream, so `SetSrcObject(null)` names one overload.
    private static string? LiveOf(string idlType, HashSet<string> proxies)
    {
        var alternatives = DomValueTypes.Alternatives(idlType).Select(a => a.TrimEnd('?')).ToList();
        return alternatives.All(proxies.Contains) ? alternatives[0] : null;
    }

    // `owner` is Interface.member, as WebObjectArgs names an argument's dictionary.
    private static List<WebMember> Operation(JsonNode m, string owner, DomValueTypes types, HashSet<string> proxies, JsonNode? callbacks)
    {
        var result = new List<WebMember>();
        var idl = owner.Substring(owner.IndexOf('.') + 1);
        var returns = m["returns"]!.AsString()!;
        var method = DomRefEmitter.Pascal(idl);
        var signatures = new HashSet<string>(StringComparer.Ordinal);
        var lists = new List<List<JsonNode>> { m["args"]?.Items ?? new List<JsonNode>() };
        lists.AddRange((m["overloads"]?.Items ?? new List<JsonNode>()).Select(o => o.Items));
        foreach (var args in lists.Where(a => !a.Any(x => x["variadic"]?.AsBoolean() == true)))
        {
            var distinct = DomRefEmitter.Prefixes(args).SelectMany(prefix => Expand(prefix, types, callbacks, proxies, owner))
                .Where(p => !Fills.Contains(owner) || !p.Any(x => x.Type.StartsWith("byte[]", StringComparison.Ordinal)))
                .Where(p => signatures.Add(string.Join(",", p.Select(x => x.Type))));
            foreach (var parameters in distinct)
            {
                if (Answer(returns, parameters, proxies) is { } answer && Call(new CallSite(m, idl, method, parameters), answer, types, proxies) is { } member)
                {
                    result.Add(member);
                }
            }
        }

        return result;
    }

    // What a call answers with. A union of live objects is the first MDN lists when nothing asks for another — the
    // default: getReader() is a ReadableStreamDefaultReader. With arguments that could pick another, it is not written.
    private static string? Answer(string returns, List<(string Type, string Name, string Arg)> parameters, HashSet<string> proxies)
    {
        var alternatives = DomValueTypes.Alternatives(returns).ToList();
        if (alternatives.Count < 2 || !alternatives.All(a => proxies.Contains(a)))
        {
            return returns;
        }

        return parameters.Count == 0 ? alternatives[0] : null;
    }

    // One overload of a call: what it declares, and the arguments it hands the chain, written into each shape of member.
    private sealed class CallSite(JsonNode m, string idl, string method, List<(string Type, string Name, string Arg)> parameters)
    {
        public string Idl => idl;

        // Always an explicit array: a lone string[] argument would otherwise BE the params array (array covariance).
        public string Args { get; } = parameters.Count == 0 ? "" : ", new object?[] { " + string.Join(", ", parameters.Select(p => p.Arg)) + " }";

        public string[] Generics { get; } = GenericsOf(parameters);

        public bool TakesHandler => parameters.Any(p => IsHandler(p.Arg));

        public WebMember Member(string note, string returns, string body, string? readsAs = null) =>
            new(m, $"MDN's <c>{idl}()</c>, called in the browser.{note}", returns, method, string.Join(", ", parameters.Select(p => p.Type + " " + p.Name)),
                string.Join(", ", parameters.Select(p => p.Name)), body, parameterTypes: string.Join(",", parameters.Select(p => p.Type)))
            { Generics = readsAs is null ? Generics : Generics.Concat(new[] { readsAs }).ToArray() };
    }

    private static WebMember? Call(CallSite call, string returns, DomValueTypes types, HashSet<string> proxies)
    {
        var (idl, args) = (call.Idl, call.Args);
        var promised = returns.StartsWith("Promise<", StringComparison.Ordinal) ? returns.Substring(8, returns.Length - 9) : null;
        if (promised is not null && proxies.Contains(promised.TrimEnd('?')))
        {
            var proxy = TypesNs + promised.TrimEnd('?');
            return call.Member(" The object it resolves to is kept: dispose of it when done.", $"ValueTask<{proxy}>",
                $"Chain.Invoke(\"{idl}\"{args}).Keep(static c => new {proxy}(c))");
        }

        if (promised is null && proxies.Contains(returns.TrimEnd('?')))
        {
            return call.Member(" Nothing runs until a member of the result is awaited.", TypesNs + returns.TrimEnd('?'), $"new(Chain.Invoke(\"{idl}\"{args}))");
        }

        if (ItemOf(promised ?? returns) is { } item && proxies.Contains(item.TrimEnd('?')))
        {
            var proxy = TypesNs + item.TrimEnd('?');
            var orNull = item.EndsWith("?", StringComparison.Ordinal);
            return call.Member(" Each object in it is kept: dispose of each when done.", $"ValueTask<{proxy}{(orNull ? "?" : "")}[]>",
                $"Chain.Invoke(\"{idl}\"{args}).{(orNull ? "KeepEachOrNull" : "KeepEach")}(static c => new {proxy}(c))");
        }

        // An `any` is read as the caller's own type (`await response.Json<Order>()`) — except a promise that settles with
        // what its callback returned (a lock request's), which is waited on, not read.
        if (string.Equals(promised ?? returns, "any", StringComparison.Ordinal))
        {
            var result = call.Generics.Length == 0 ? "T" : "TResult";
            return promised is not null && call.TakesHandler
                ? call.Member("", "ValueTask", $"Chain.Call(\"{idl}\"{args})")
                : call.Member(" Its result is read as your own type.", $"ValueTask<{result}>", $"Chain.CallAny<{result}>(\"{idl}\"{args})", result);
        }

        if (types.CSharp(returns, returned: true) is not { } value)
        {
            return null;
        }

        // A record of your own type (a stream read's ReadableStreamReadResult<T>) is read as an `any` is.
        if (value.EndsWith("<T>", StringComparison.Ordinal))
        {
            return call.Member(" Its result is read as your own type.", $"ValueTask<{value}>", $"Chain.CallAny<{value}>(\"{idl}\"{args})", "T");
        }

        return string.Equals(value, "void", StringComparison.Ordinal)
            ? call.Member("", "ValueTask", $"Chain.Call(\"{idl}\"{args})")
            : call.Member("", $"ValueTask<{value}>", $"Chain.Call<{value}>(\"{idl}\"{args})");
    }

    // The item type of a sequence or frozen array, `sequence<USBDevice>` → USBDevice; null for anything else.
    private static string? ItemOf(string idl)
    {
        var bare = idl.TrimEnd('?');
        var open = DomValueTypes.Arrays.FirstOrDefault(a => bare.StartsWith(a, StringComparison.Ordinal) && bare.EndsWith(">", StringComparison.Ordinal));
        return open is null ? null : bare.Substring(open.Length, bare.Length - open.Length - 1);
    }

    public void WriteInstance(StringBuilder sb) => Write(sb, "    ", "public " + _modifiers, _body);

    // An instance member only WebAssembly runs, in Rask.Wasm: an extension member of the proxy, over its chain.
    public void WriteExtension(StringBuilder sb, string receiver) => Write(sb, "        ", "public ", Rechain(receiver + ".Chain."));

    // Whether it is an attribute's read: a property, where a write or a call takes parameters.
    public bool IsRead => _parameters is null;

    // The Rask.Web types it hands over or answers with, by MDN name: a live object among them is what an element ref in
    // Core cannot reach.
    public IEnumerable<string> WebTypes => WebHost.TypesIn(_returns + " " + _parameters);

    // An element ref's member, an extension of IElementRef<T> named `element`, over a chain from its element.
    public void WriteOnElement(StringBuilder sb) =>
        Write(sb, "        ", "public ", Rechain("global::Rask.Web.JsChain.Element(element.Target)."));

    // A static or constructor on its class in Rask.Web, no base to hide there, so plainly static; or, indented, inside
    // Rask.Wasm's static extension of that class.
    public void WriteFacade(StringBuilder sb, string indent = "    ") => Write(sb, indent, "public static ", _body);

    private void Write(StringBuilder sb, string indent, string modifiers, string body)
    {
        DomEmitter.Doc(sb, indent, _summary, _data);
        sb.Append(indent).Append(modifiers).Append(_returns).Append(' ').Append(Name).Append(TypeParameters);
        if (_parameters is not null)
        {
            sb.Append('(').Append(_parameters).Append(')');
        }

        sb.Append(" => ").Append(body).AppendLine(";");
    }

    // A global's member, forwarded to the global's instance: `instance` is the class's own Instance, or, from Rask.Wasm's
    // static extension of the global, the global's by name.
    public void WriteStatic(StringBuilder sb, string indent = "    ", string instance = "Instance")
    {
        DomEmitter.Doc(sb, indent, _summary, _data);
        sb.Append(indent).Append("public static ").Append(_returns).Append(' ').Append(Name).Append(TypeParameters);
        if (_parameters is not null)
        {
            sb.Append('(').Append(_parameters).Append(')');
        }

        sb.Append(" => ").Append(instance).Append('.').Append(Name).Append(TypeArguments);
        if (_parameters is not null)
        {
            sb.Append('(').Append(_arguments).Append(')');
        }

        sb.AppendLine(";");
    }
}
