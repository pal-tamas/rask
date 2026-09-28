// The element EVENTS half of the MDN build step: the events every element can fire (GlobalEventHandlers), the
// argument types they are dispatched with (MouseEvent : UIEvent : Event, as MDN defines them), the dispatch that
// feeds a handler its typed argument, and the table the browser reads to build each payload. Everything here is
// MDN's data; what is Rask's is in the named policy tables at the top.
#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Rask.Generators.Json;

namespace Rask.Core.Dom.Build
{
    internal static class DomEventEmitter
    {
        // Events a typed control owns: its binding writes the model on them (HTMLInputElement<T>'s input/change,
        // HTMLFormElement<T>'s submit), so they are not a second, universal property on every element.
        private static readonly HashSet<string> OwnedEvents = new(StringComparer.Ordinal) { "input", "change", "submit", "formdata" };

        // Events whose handler is only useful if the browser's default is prevented: a drop needs every dragover
        // prevented (HTML drag and drop), and a contextmenu handler exists to replace the browser's menu.
        private static readonly HashSet<string> PreventedEvents = new(StringComparer.Ordinal) { "contextmenu", "dragover", "drop" };

        // What of the TARGET element travels with an event, as e.Target: the scroll box for a scroll, the playback
        // state for a media event. MDN's JavaScript reads these as e.target.scrollTop / e.target.currentTime.
        private static readonly string[] ScrollState = { "scrollTop", "scrollLeft", "scrollHeight", "scrollWidth", "clientHeight", "clientWidth" };
        private static readonly string[] MediaState = { "currentTime", "duration", "paused", "ended", "volume", "muted", "playbackRate" };

        // The clipboard/drag formats a DataTransfer snapshot answers GetData for.
        private static readonly string[] DataFormats = { "text/plain", "text/html", "text/uri-list" };

        // The words an event name is made of, so `loadedmetadata` becomes OnLoadedMetadata. An event name the
        // list cannot split fails the build (RASKDOM002): add the word here.
        private static readonly string[] Words =
        {
            "composition", "visibility", "fullscreen", "animation", "transition", "iteration", "restored", "metadata",
            "duration", "security", "violation", "content", "state", "auto", "in",
            "selection", "pointer", "capture", "context", "through", "emptied", "stalled", "suspend", "waiting",
            "playing", "seeking", "command", "invalid", "before", "cancel", "change", "toggle", "select", "scroll",
            "update", "volume", "policy", "submit", "loaded", "seeked", "cancel", "resize", "abort", "match",
            "start", "input", "close", "enter", "leave", "focus", "click", "mouse", "touch", "wheel", "paste",
            "pause", "reset", "ended", "error", "slot", "lost", "menu", "copy", "drag", "drop", "down", "move",
            "over", "load", "play", "rate", "time", "blur", "form", "data", "raw", "end", "run", "aux", "can",
            "cue", "cut", "dbl", "key", "out", "got", "up", "progress",
        };

        private static bool IsPlain(string type) => CSharp(type) is not null;

        private static string? CSharp(string idl) => idl.TrimEnd('?') switch
        {
            "DOMString" or "USVString" or "ByteString" or "CSSOMString" => idl.EndsWith("?", StringComparison.Ordinal) ? "string?" : "string",
            "boolean" => "bool",
            "short" or "unsigned short" or "long" or "unsigned long" or "octet" or "byte" => "int",
            "long long" or "unsigned long long" or "EpochTimeStamp" => "long",
            "double" or "unrestricted double" or "float" or "unrestricted float" or "DOMHighResTimeStamp" => "double",
            _ => null,
        };

        private static string Reader(string cs) => cs switch
        {
            "string" or "string?" => "ReadString",
            "bool" => "ReadBool",
            "int" => "ReadInt",
            "long" => "ReadLong",
            _ => "ReadDouble",
        };

        private static string Pascal(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s.Substring(1);

        internal static string HandlerName(string type)
        {
            var sb = new StringBuilder("On");
            var i = 0;
            while (i < type.Length)
            {
                var word = Words.Where(w => string.CompareOrdinal(type, i, w, 0, w.Length) == 0).OrderByDescending(w => w.Length).FirstOrDefault()
                           ?? throw new DomEmitException($"RASKDOM002: the event name '{type}' has a part DomEventEmitter.Words cannot split at '{type.Substring(i)}'. Add the word to the list.");
                sb.Append(Pascal(word));
                i += word.Length;
            }

            return sb.ToString();
        }

        private sealed class Member
        {
            public string Idl = "";
            public string Prop = "";
            public string Cs = "";
            // 0 plain, 1 list of a snapshot type, 2 a snapshot object
            public int Kind;
            public string? Of;
        }

        internal static void Emit(JsonNode root, List<KeyValuePair<string, string>> files)
        {
            var interfaces = root["interfaces"]!;
            var events = root["events"]?.Items.Where(e => !OwnedEvents.Contains(e["type"]!.AsString()!)).ToList() ?? new List<JsonNode>();
            if (events.Count == 0)
            {
                return;
            }

            // The argument interfaces and their ancestors, base first.
            var argTypes = new List<string>();
            void Add(string? name)
            {
                if (name is null || argTypes.Contains(name) || interfaces[name] is null)
                {
                    return;
                }

                Add(interfaces[name]!["parent"]?.AsString());
                argTypes.Add(name);
            }

            foreach (var e in events)
            {
                Add(e["interface"]?.AsString());
            }

            var snapshots = new List<string>();
            var members = new Dictionary<string, List<Member>>(StringComparer.Ordinal);
            foreach (var name in argTypes)
            {
                members[name] = MembersOf(interfaces, name, snapshots);
            }

            foreach (var s in snapshots.ToList())
            {
                members[s] = MembersOf(interfaces, s, null);
            }

            var cs = new StringBuilder();
            Header(cs);
            cs.AppendLine("using System.Text.Json;");
            cs.AppendLine("using Rask.Core.Live;");
            cs.AppendLine();
            cs.AppendLine("namespace Rask.Core;");
            var hasChild = new HashSet<string>(argTypes.Select(t => interfaces[t]!["parent"]?.AsString()).OfType<string>(), StringComparer.Ordinal);
            foreach (var name in argTypes)
            {
                var parent = interfaces[name]!["parent"]?.AsString();
                var isRoot = parent is null || !argTypes.Contains(parent);
                EmitClass(cs, name, interfaces[name]!, isRoot ? null : parent, members[name], sealedType: !hasChild.Contains(name), isEvent: true);
            }

            foreach (var s in snapshots)
            {
                EmitClass(cs, s, interfaces[s]!, null, members[s], sealedType: true, isEvent: false);
            }

            EmitTarget(cs, interfaces);
            files.Add(new KeyValuePair<string, string>("Events.g.cs", cs.ToString()));

            files.Add(new KeyValuePair<string, string>("ElementEvents.g.cs", ElementEvents(events)));
            files.Add(new KeyValuePair<string, string>("DomEventDispatch.g.cs", Dispatch(events, argTypes, interfaces)));
            files.Add(new KeyValuePair<string, string>("rask-dom-events.ts", Script(events, argTypes, snapshots, members, interfaces)));
        }

        // A member of an event interface that crosses the wire: plain values, a list of a plain-valued interface
        // (TouchList → Touch), or a DataTransfer. Element, Window and EventTarget members stay in the browser.
        private static List<Member> MembersOf(JsonNode interfaces, string name, List<string>? snapshots)
        {
            var result = new List<Member>();
            foreach (var m in interfaces[name]!["members"]?.Items ?? new List<JsonNode>())
            {
                if (m["kind"]?.AsString() != "attribute" || m["name"]?.AsString() is not { } idl)
                {
                    continue;
                }

                var type = m["type"]?.AsString() ?? "";
                if (CSharp(type) is { } plain)
                {
                    result.Add(new Member { Idl = idl, Prop = Pascal(idl), Cs = plain });
                    continue;
                }

                var held = type.TrimEnd('?');
                if (snapshots is null && held is not ("FrozenArray<DOMString>" or "sequence<DOMString>"))
                {
                    continue;
                }

                if (held is "FrozenArray<DOMString>" or "sequence<DOMString>")
                {
                    result.Add(new Member { Idl = idl, Prop = Pascal(idl), Cs = "global::System.Collections.Generic.IReadOnlyList<string>", Kind = 3 });
                    continue;
                }

                if (held == "DataTransfer" && snapshots is not null)
                {
                    if (!snapshots.Contains(held))
                    {
                        snapshots.Add(held);
                    }

                    result.Add(new Member { Idl = idl, Prop = Pascal(idl), Cs = "DataTransfer?", Kind = 2, Of = held });
                    continue;
                }

                if (snapshots is null)
                {
                    continue;
                }

                var list = interfaces[held];
                var item = list?["members"]?.Items.FirstOrDefault(x => x["kind"]?.AsString() == "operation" && x["name"]?.AsString() == "item");
                var itemType = item?["returns"]?.AsString()?.TrimEnd('?');
                if (itemType is not null && interfaces[itemType] is not null)
                {
                    if (!snapshots.Contains(itemType))
                    {
                        snapshots.Add(itemType);
                    }

                    result.Add(new Member { Idl = idl, Prop = Pascal(idl), Cs = $"global::System.Collections.Generic.IReadOnlyList<{itemType}>", Kind = 1, Of = itemType });
                }
            }

            return result;
        }

        private static void EmitClass(StringBuilder cs, string name, JsonNode iface, string? parent, List<Member> members, bool sealedType, bool isEvent)
        {
            cs.AppendLine();
            cs.Append("/// <summary>The DOM's <c>").Append(name).AppendLine("</c>, as the browser sent it.</summary>");
            Remarks(cs, "", iface);
            cs.Append("public ").Append(sealedType ? "sealed " : "").Append("class ").Append(name);
            if (parent is not null)
            {
                cs.Append(" : ").Append(parent);
            }

            cs.AppendLine();
            cs.AppendLine("{");
            cs.Append("    /// <summary>An empty ").Append(name).AppendLine(", for a test to fill.</summary>");
            cs.Append("    public ").Append(name).AppendLine("()");
            cs.AppendLine("    {");
            cs.AppendLine("    }");
            cs.AppendLine();
            cs.Append("    internal ").Append(name).Append("(JsonElement p)");
            if (parent is not null)
            {
                cs.Append(" : base(p)");
            }

            cs.AppendLine();
            cs.AppendLine("    {");
            foreach (var m in members)
            {
                cs.Append("        ").Append(m.Prop).Append(" = ");
                switch (m.Kind)
                {
                    case 0:
                        cs.Append("EventPayload.").Append(Reader(m.Cs)).Append("(p, \"").Append(m.Idl).Append("\")");
                        break;
                    case 1:
                        cs.Append("EventPayload.ReadList(p, \"").Append(m.Idl).Append("\", static x => new ").Append(m.Of).Append("(x))");
                        break;
                    case 3:
                        cs.Append("EventPayload.ReadStrings(p, \"").Append(m.Idl).Append("\")");
                        break;
                    default:
                        cs.Append("p.TryGetProperty(\"").Append(m.Idl).Append("\", out var ").Append(m.Idl).Append(") && ").Append(m.Idl)
                            .Append(".ValueKind == JsonValueKind.Object ? new DataTransfer(").Append(m.Idl).Append(") : null");
                        break;
                }

                cs.AppendLine(";");
            }

            if (isEvent && parent is null)
            {
                cs.AppendLine("        Target = p.TryGetProperty(\"target\", out var target) && target.ValueKind == JsonValueKind.Object ? new EventTarget(target) : null;");
            }

            if (name == "DataTransfer")
            {
                cs.AppendLine("        _data = EventPayload.ReadMap(p, \"data\");");
            }

            cs.AppendLine("    }");
            foreach (var m in members)
            {
                var data = FindMember(iface, m.Idl);
                cs.AppendLine();
                cs.Append("    /// <summary>The event's <c>").Append(m.Idl).AppendLine("</c>.</summary>");
                if (data is not null)
                {
                    Remarks(cs, "    ", data);
                }

                cs.Append("    public ").Append(m.Cs).Append(' ').Append(m.Prop).Append(" { get; init; }");
                cs.AppendLine(m.Cs == "string" ? " = \"\";" : m.Kind is 1 or 3 ? " = [];" : "");
            }

            if (isEvent && parent is null)
            {
                cs.AppendLine();
                cs.AppendLine("    /// <summary>");
                cs.AppendLine("    ///     What of the element the event was dispatched to travels with it: the scroll box for a scroll, the");
                cs.AppendLine("    ///     playback state for a media event — <c>e.Target.ScrollTop</c>, as MDN's <c>e.target.scrollTop</c>.");
                cs.AppendLine("    ///     Null for an event that carries none.");
                cs.AppendLine("    /// </summary>");
                cs.AppendLine("    public EventTarget? Target { get; init; }");
            }

            if (name == "DataTransfer")
            {
                cs.AppendLine();
                cs.AppendLine("    private readonly global::System.Collections.Generic.IReadOnlyDictionary<string, string>? _data;");
                cs.AppendLine();
                cs.AppendLine("    /// <summary>The data in <paramref name=\"format\" />, as the browser read it when the event fired; empty when it held none.</summary>");
                cs.Append("    /// <remarks>Sent for ").Append(string.Join(", ", DataFormats.Select(f => $"<c>{f}</c>"))).AppendLine(".</remarks>");
                cs.AppendLine("    public string GetData(string format) => _data is not null && _data.TryGetValue(format, out var value) ? value : \"\";");
            }

            cs.AppendLine("}");
        }

        private static JsonNode? FindMember(JsonNode iface, string name) =>
            iface["members"]?.Items.FirstOrDefault(m => m["name"]?.AsString() == name);

        // e.Target: the target element's state the policy sends, typed from Element and HTMLMediaElement's IDL.
        private static void EmitTarget(StringBuilder cs, JsonNode interfaces)
        {
            var props = new List<(string Idl, string Cs, JsonNode? Data)>();
            foreach (var (iface, names) in new[] { ("Element", ScrollState), ("HTMLMediaElement", MediaState) })
            {
                foreach (var n in names)
                {
                    var m = interfaces[iface] is { } i ? FindMember(i, n) : null;
                    props.Add((n, CSharp(m?["type"]?.AsString() ?? "double") ?? "double", m));
                }
            }

            cs.AppendLine();
            cs.AppendLine("/// <summary>");
            cs.AppendLine("///     The element an event was dispatched to, as far as its state travels with the event: the scroll box of a");
            cs.AppendLine("///     scroll, the playback state of a media event. Each value is MDN's property of that element; one the event");
            cs.AppendLine("///     does not carry reads as its default.");
            cs.AppendLine("/// </summary>");
            cs.AppendLine("public sealed class EventTarget");
            cs.AppendLine("{");
            cs.AppendLine("    /// <summary>An empty target, for a test to fill.</summary>");
            cs.AppendLine("    public EventTarget()");
            cs.AppendLine("    {");
            cs.AppendLine("    }");
            cs.AppendLine();
            cs.AppendLine("    internal EventTarget(JsonElement p)");
            cs.AppendLine("    {");
            foreach (var (idl, type, _) in props)
            {
                cs.Append("        ").Append(Pascal(idl)).Append(" = EventPayload.").Append(Reader(type)).Append("(p, \"").Append(idl).AppendLine("\");");
            }

            cs.AppendLine("    }");
            foreach (var (idl, type, data) in props)
            {
                cs.AppendLine();
                cs.Append("    /// <summary>The target's <c>").Append(idl).AppendLine("</c>.</summary>");
                if (data is not null)
                {
                    Remarks(cs, "    ", data);
                }

                cs.Append("    public ").Append(type).Append(' ').Append(Pascal(idl)).AppendLine(" { get; init; }");
            }

            cs.AppendLine("}");
        }

        private static string ElementEvents(List<JsonNode> events)
        {
            var cs = new StringBuilder();
            Header(cs);
            cs.AppendLine();
            cs.AppendLine("namespace Rask.Core;");
            cs.AppendLine();
            cs.AppendLine("public abstract partial class Element");
            cs.AppendLine("{");
            cs.AppendLine("    // Every event an element can fire, in the IDL's GlobalEventHandlers order: the emit order.");
            cs.Append("    internal static readonly string[] GlobalEventOrder = [");
            cs.Append(string.Join(", ", events.Select(e => "\"" + e["type"]!.AsString() + "\"")));
            cs.AppendLine("];");
            foreach (var e in events)
            {
                var type = e["type"]!.AsString()!;
                var arg = e["interface"]?.AsString() ?? "Event";
                cs.AppendLine();
                cs.Append("    /// <summary>The <c>").Append(type).Append("</c> event, with its <c>").Append(arg)
                    .Append("</c>. A handler may take it, or nothing: <c>e =&gt; …</c> or <c>() =&gt; …</c>.</summary>").AppendLine();
                Remarks(cs, "    ", e);
                cs.Append("    public Callback<").Append(arg).Append("> ").Append(HandlerName(type))
                    .Append(" { get => Handler<").Append(arg).Append(">(\"").Append(type).Append("\"); set => SetHandler(\"").Append(type)
                    .AppendLine("\", value.Handler); }");
            }

            cs.AppendLine("}");
            return cs.ToString();
        }

        // Feeding a handler its typed argument, and which frames a handler may be fed. A handler that takes T is fed
        // the events whose interface is T or derives from it (a MouseEvent handler takes a click's PointerEvent); a
        // handler that takes nothing is fed any of them.
        private static string Dispatch(List<JsonNode> events, List<string> argTypes, JsonNode interfaces)
        {
            bool Derives(string type, string ancestor)
            {
                for (var t = type; t is not null; t = interfaces[t]?["parent"]?.AsString())
                {
                    if (t == ancestor)
                    {
                        return true;
                    }
                }

                return false;
            }

            var cs = new StringBuilder();
            Header(cs);
            cs.AppendLine("using System.Text.Json;");
            cs.AppendLine();
            cs.AppendLine("namespace Rask.Core.Live;");
            cs.AppendLine();
            cs.AppendLine("internal static class DomEventDispatch");
            cs.AppendLine("{");
            cs.AppendLine("    // Every event frame type, as UTF-8 so a frame's type is matched without allocating.");
            cs.Append("    private static readonly byte[][] Types = [");
            cs.Append(string.Join(", ", events.Select(e => "\"" + e["type"]!.AsString() + "\"u8.ToArray()")));
            cs.AppendLine("];");
            cs.AppendLine();
            cs.AppendLine("    // Types[i] is dispatched with Interfaces[i], an index into the argument types below.");
            cs.Append("    private static readonly int[] Interfaces = [");
            cs.Append(string.Join(", ", events.Select(e => argTypes.IndexOf(e["interface"]?.AsString() ?? "Event"))));
            cs.AppendLine("];");
            cs.AppendLine();
            cs.AppendLine("    // Derives[a][b]: argument type a is, or derives from, argument type b.");
            cs.AppendLine("    private static readonly bool[][] Derives =");
            cs.AppendLine("    [");
            foreach (var a in argTypes)
            {
                cs.Append("        [").Append(string.Join(", ", argTypes.Select(b => Derives(a, b) ? "true" : "false"))).AppendLine("],");
            }

            cs.AppendLine("    ];");
            cs.AppendLine();
            cs.AppendLine("    /// <summary>The index of the event frame <paramref name=\"type\" /> names, or -1 when it names none.</summary>");
            cs.AppendLine("    internal static int IndexOf(JsonElement type)");
            cs.AppendLine("    {");
            cs.AppendLine("        // The frame's own bytes, bucketed by length: every event is on the per-frame path, so no scan of all of");
            cs.AppendLine("        // them. An escaped name (\\u0063lick) takes the scan, which matches it as ValueEquals always has.");
            cs.AppendLine("        var raw = global::System.Runtime.InteropServices.JsonMarshal.GetRawUtf8Value(type);");
            cs.AppendLine("        if (type.ValueKind != JsonValueKind.String || raw.Length < 2 || raw.IndexOf((byte)'\\\\') >= 0)");
            cs.AppendLine("        {");
            cs.AppendLine("            return Scan(type);");
            cs.AppendLine("        }");
            cs.AppendLine();
            cs.AppendLine("        var name = raw[1..^1];");
            cs.AppendLine("        switch (name.Length)");
            cs.AppendLine("        {");
            foreach (var bucket in events.Select((e, i) => (Name: e["type"]!.AsString()!, Index: i)).GroupBy(e => e.Name.Length).OrderBy(g => g.Key))
            {
                cs.Append("            case ").Append(bucket.Key).AppendLine(":");
                foreach (var (name, index) in bucket)
                {
                    cs.Append("                if (name.SequenceEqual(\"").Append(name).Append("\"u8)) return ").Append(index).AppendLine(";");
                }

                cs.AppendLine("                return -1;");
            }

            cs.AppendLine("            default:");
            cs.AppendLine("                return -1;");
            cs.AppendLine("        }");
            cs.AppendLine("    }");
            cs.AppendLine();
            cs.AppendLine("    private static int Scan(JsonElement type)");
            cs.AppendLine("    {");
            cs.AppendLine("        if (type.ValueKind != JsonValueKind.String)");
            cs.AppendLine("        {");
            cs.AppendLine("            return -1;");
            cs.AppendLine("        }");
            cs.AppendLine();
            cs.AppendLine("        for (var i = 0; i < Types.Length; i++)");
            cs.AppendLine("        {");
            cs.AppendLine("            if (type.ValueEquals(Types[i]))");
            cs.AppendLine("            {");
            cs.AppendLine("                return i;");
            cs.AppendLine("            }");
            cs.AppendLine("        }");
            cs.AppendLine();
            cs.AppendLine("        return -1;");
            cs.AppendLine("    }");
            cs.AppendLine();
            cs.AppendLine("    /// <summary>Every event name, for a caller that holds one as a string.</summary>");
            cs.Append("    internal static readonly global::System.Collections.Frozen.FrozenSet<string> Names = global::System.Collections.Frozen.FrozenSet.ToFrozenSet([");
            cs.Append(string.Join(", ", events.Select(e => "\"" + e["type"]!.AsString() + "\"")));
            cs.AppendLine("], global::System.StringComparer.Ordinal);");
            cs.AppendLine();
            cs.AppendLine("    /// <summary>Whether <paramref name=\"handler\" /> takes a DOM event argument (MouseEvent, KeyboardEvent, …).</summary>");
            cs.AppendLine("    internal static bool IsDomHandler(Delegate handler) => ArgumentOf(handler) >= 0;");
            cs.AppendLine();
            cs.AppendLine("    /// <summary>Whether <paramref name=\"handler\" /> may be fed event frame <paramref name=\"index\" />.</summary>");
            cs.AppendLine("    internal static bool Accepts(int index, Delegate handler)");
            cs.AppendLine("    {");
            cs.AppendLine("        if (handler is Action or Func<Task>)");
            cs.AppendLine("        {");
            cs.AppendLine("            return true;");
            cs.AppendLine("        }");
            cs.AppendLine();
            cs.AppendLine("        var takes = ArgumentOf(handler);");
            cs.AppendLine("        return takes >= 0 && Derives[Interfaces[index]][takes];");
            cs.AppendLine("    }");
            cs.AppendLine();
            cs.AppendLine("    // The argument type a DOM event handler takes, or -1 for any other handler. Base types are tested first:");
            cs.AppendLine("    // delegates are contravariant, so an Action<MouseEvent> is also an Action<PointerEvent>, and only walking");
            cs.AppendLine("    // down from the base lands on the type the handler declared.");
            cs.AppendLine("    private static int ArgumentOf(Delegate handler) => handler switch");
            cs.AppendLine("    {");
            foreach (var t in argTypes.OrderBy(t => Depth(t, interfaces)))
            {
                cs.Append("        Action<global::Rask.Core.").Append(t).Append("> or Func<global::Rask.Core.").Append(t).Append(", Task> => ")
                    .Append(argTypes.IndexOf(t)).AppendLine(",");
            }

            cs.AppendLine("        _ => -1,");
            cs.AppendLine("    };");
            cs.AppendLine();
            cs.AppendLine("    /// <summary>");
            cs.AppendLine("    ///     Runs a DOM event handler with its argument read from <paramref name=\"payload\" />: a synchronous one now,");
            cs.AppendLine("    ///     an asynchronous one handed back in <paramref name=\"pending\" /> for the caller to await. False for any");
            cs.AppendLine("    ///     other handler. The argument is the event's own interface when the frame names the event, so a click's");
            cs.AppendLine("    ///     PointerEvent reaches an Action&lt;MouseEvent&gt; whole; a frame that names none builds the handler's.");
            cs.AppendLine("    /// </summary>");
            cs.AppendLine("    internal static bool TryInvoke(Delegate handler, JsonElement payload, out Func<Task>? pending)");
            cs.AppendLine("    {");
            cs.AppendLine("        pending = null;");
            cs.AppendLine("        var takes = ArgumentOf(handler);");
            cs.AppendLine("        if (takes < 0)");
            cs.AppendLine("        {");
            cs.AppendLine("            return false;");
            cs.AppendLine("        }");
            cs.AppendLine();
            cs.AppendLine("        var index = payload.ValueKind == JsonValueKind.Object && payload.TryGetProperty(\"type\", out var type) ? IndexOf(type) : -1;");
            cs.AppendLine("        var e = Create(index >= 0 && Derives[Interfaces[index]][takes] ? Interfaces[index] : takes, payload);");
            cs.AppendLine("        switch (handler)");
            cs.AppendLine("        {");
            foreach (var t in argTypes.OrderBy(t => Depth(t, interfaces)))
            {
                cs.Append("            case Action<global::Rask.Core.").Append(t).Append("> a").Append(t).AppendLine(":");
                cs.Append("                a").Append(t).Append("((global::Rask.Core.").Append(t).AppendLine(")e);");
                cs.AppendLine("                return true;");
                cs.Append("            case Func<global::Rask.Core.").Append(t).Append(", Task> f").Append(t).AppendLine(":");
                cs.Append("                pending = Pending(f").Append(t).Append(", (global::Rask.Core.").Append(t).AppendLine(")e);");
                cs.AppendLine("                return true;");
            }

            cs.AppendLine("            default:");
            cs.AppendLine("                return false;");
            cs.AppendLine("        }");
            cs.AppendLine("    }");
            cs.AppendLine();
            cs.AppendLine("    // The closure lives here, not in TryInvoke: a lambda there would hoist every case's captures into one");
            cs.AppendLine("    // object allocated on entry, which every handler — parameterless ones included — would pay for.");
            cs.AppendLine("    private static Func<Task> Pending<T>(Func<T, Task> handler, T e) => () => handler(e);");
            cs.AppendLine();
            cs.AppendLine("    private static global::Rask.Core.Event Create(int type, JsonElement payload) => type switch");
            cs.AppendLine("    {");
            for (var i = 0; i < argTypes.Count; i++)
            {
                if (argTypes[i] != "Event")
                {
                    cs.Append("        ").Append(i).Append(" => new global::Rask.Core.").Append(argTypes[i]).AppendLine("(payload),");
                }
            }

            cs.AppendLine("        _ => new global::Rask.Core.Event(payload),");
            cs.AppendLine("    };");
            cs.AppendLine("}");
            return cs.ToString();
        }

        private static int Depth(string type, JsonNode interfaces)
        {
            var d = 0;
            for (var t = interfaces[type]?["parent"]?.AsString(); t is not null; t = interfaces[t]?["parent"]?.AsString())
            {
                d++;
            }

            return d;
        }

        // The browser half: for each event, what to read off it — the interface's fields (inherited ones included),
        // the target state the policy sends, and whether it bubbles and is prevented.
        private static string Script(List<JsonNode> events, List<string> argTypes, List<string> snapshots, Dictionary<string, List<Member>> members, JsonNode interfaces)
        {
            List<Member> Flattened(string name)
            {
                var chain = new List<string>();
                for (var t = name; t is not null && members.ContainsKey(t); t = interfaces[t]?["parent"]?.AsString())
                {
                    chain.Insert(0, t);
                }

                return chain.SelectMany(t => members[t]).ToList();
            }

            string Fields(List<Member> ms) => "[" + string.Join(", ", ms.Select(m =>
                m.Kind is 0 or 3 ? $"\"{m.Idl}\"" : $"[\"{m.Idl}\", \"{m.Of}\"]")) + "]";

            var ts = new StringBuilder();
            ts.AppendLine("// <auto-generated/> from src/Rask.Core/Dom/mdn.snapshot.json by Rask.Dom.targets — do not edit.");
            ts.AppendLine("// The fields each event interface sends (a [name, snapshot] pair for a list or a DataTransfer), and per event:");
            ts.AppendLine("// its interface, whether it bubbles, whether Rask prevents its default, and which target state travels with it.");
            ts.AppendLine();
            ts.AppendLine("export type DomField = string | [string, string];");
            ts.AppendLine();
            ts.AppendLine("export const domInterfaces: Record<string, readonly DomField[]> = {");
            foreach (var t in argTypes.Concat(snapshots))
            {
                ts.Append("  ").Append(t).Append(": ").Append(Fields(snapshots.Contains(t) ? members[t] : Flattened(t))).AppendLine(",");
            }

            ts.AppendLine("};");
            ts.AppendLine();
            ts.Append("export const domTargetScroll: readonly string[] = [").Append(string.Join(", ", ScrollState.Select(s => $"\"{s}\""))).AppendLine("];");
            ts.Append("export const domTargetMedia: readonly string[] = [").Append(string.Join(", ", MediaState.Select(s => $"\"{s}\""))).AppendLine("];");
            ts.Append("export const domDataFormats: readonly string[] = [").Append(string.Join(", ", DataFormats.Select(s => $"\"{s}\""))).AppendLine("];");
            ts.AppendLine();
            ts.AppendLine("// [type, interface, bubbles, prevented, target state: 0 none, 1 scroll box, 2 media]");
            ts.AppendLine("export const domEvents: readonly (readonly [string, string, boolean, boolean, number])[] = [");
            foreach (var e in events)
            {
                var type = e["type"]!.AsString()!;
                var target = type is "scroll" or "scrollend" ? 1 : e["on"]?.AsString() == "HTMLMediaElement" ? 2 : 0;
                ts.Append("  [\"").Append(type).Append("\", \"").Append(e["interface"]?.AsString() ?? "Event").Append("\", ")
                    .Append(e["bubbles"]?.AsBoolean() == true ? "true" : "false").Append(", ")
                    .Append(PreventedEvents.Contains(type) ? "true" : "false").Append(", ").Append(target).AppendLine("],");
            }

            ts.AppendLine("];");
            return ts.ToString();
        }

        private static void Header(StringBuilder cs)
        {
            cs.AppendLine("// <auto-generated/> from src/Rask.Core/Dom/mdn.snapshot.json by Rask.Dom.targets — do not edit.");
            cs.AppendLine("#nullable enable");
        }

        private static void Remarks(StringBuilder cs, string indent, JsonNode data)
        {
            var parts = new List<string>();
            if (data["experimental"]?.AsBoolean() == true)
            {
                parts.Add("<b>Experimental.</b>");
            }

            if (data["support"] is { Kind: JsonKind.Object } support && support.Members.Count > 0)
            {
                parts.Add(string.Join(" · ", support.Members.Select(p => $"{DomEmitter.BrowserName(p.Key)} {p.Value.AsString()}")) + ".");
            }

            if (data["mdn"]?.AsString() is { } mdn)
            {
                parts.Add($"<see href=\"{mdn}\">MDN</see>");
            }

            if (data["spec"]?.AsString() is { } spec)
            {
                parts.Add($"<see href=\"{spec}\">Spec</see>");
            }

            if (parts.Count > 0)
            {
                cs.Append(indent).Append("/// <remarks>").Append(string.Join(" ", parts)).AppendLine("</remarks>");
            }
        }
    }
}
