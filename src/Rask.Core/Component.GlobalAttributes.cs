using System.Buffers;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using Rask.Core.Live;

namespace Rask.Core;

public abstract partial class Component
{
    // Backing store for Element.Ref, hoisted into the lazy LiveState so a plain element (the
    // overwhelming majority — refs are opt-in) keeps `_live` null and pays nothing for the
    // feature. The setter only forces a LiveState allocation when an actual ref is assigned;
    // setting `default` on a ref-less element is a no-op.
    internal ElementRef? ElementRefInternal
    {
        get => _live?.ElementRef;
        set
        {
            if (value is not null || _live is not null)
            {
                Live.ElementRef = value;
            }
        }
    }

    // Backing store for Element.Role/TabIndex/Aria, hoisted into the lazy LiveState for the same
    // reason as Ref: accessibility attributes are opt-in and rare, so a plain element keeps `_live`
    // null and adds zero footprint. The setters only force a LiveState allocation when an actual
    // value is assigned; setting null on an element that never used the feature is a no-op.
    internal string? RoleInternal
    {
        get => _live?.Role;
        set
        {
            if (value is not null || _live is not null)
            {
                Live.Role = value;
            }
        }
    }

    internal int? TabIndexInternal
    {
        get => _live?.TabIndex;
        set
        {
            if (value is not null || _live is not null)
            {
                Live.TabIndex = value;
            }
        }
    }

    internal IReadOnlyDictionary<string, string?>? AriaInternal
    {
        get => _live?.Aria;
        set
        {
            if (value is not null || _live is not null)
            {
                Live.Aria = value;
            }
        }
    }

    // Backing store for Element.Attributes, on the same terms as AriaInternal: reading never allocates,
    // and writing only forces the LiveState into existence once a value is actually assigned.
    internal IReadOnlyDictionary<string, string?>? ExtraAttrsInternal
    {
        get => _live?.ExtraAttrs;
        set
        {
            if (value is not null || _live is not null)
            {
                Live.ExtraAttrs = value;
            }
        }
    }

    // The rest of HTML's global attributes (#693), on a side object rather than a typed field each.
    // The reasoning is the one LiveState.ExtraAttrs records: LiveState is allocated per node on a
    // mounted page, so every field on it costs ~8 B on every node of every live session (~56 KB per
    // field on a 1,000-row page). Six typed fields would be ~336 KB there; one reference is ~56 KB,
    // and only an element that actually names one of these attributes allocates the side object.
    //
    // Reads go through `_live?.Globals?.X`, so an element that names none pays two null checks and no
    // allocation — and Element.WriteAttributes fetches the object ONCE via GlobalAttrsInternal rather
    // than re-walking it per attribute, which makes the common element cheaper than a field each.
    //
    // The build adds the rest of MDN's global attributes (accesskey, nonce, slot, …) to this same object, in
    // a generated partial (Rask.Dom.targets), for the same reason: they are rare, and HTMLElement is the base
    // of every HTML element.
    //
    // SVG's rarer presentation attributes (cursor, marker-end, …) are generated onto a subclass, which an SVG element
    // allocates in its place (NewGlobalAttrs): an HTML element's side object carries no field for them.
    internal partial class GlobalAttrs
    {
        public string? Lang;
        public string? Dir;
        public string? Popover;
        public string? ContentEditable;
        public bool? Spellcheck;
        public bool? Translate;
    }

    // The whole side object in one read, for the writer's single null check.
    internal GlobalAttrs? GlobalAttrsInternal => _live?.Globals;

    private GlobalAttrs Globals => Live.Globals ??= NewGlobalAttrs();

    // The side object this element's attributes need: SVGElement's carries SVG's as well.
    private protected virtual GlobalAttrs NewGlobalAttrs() => new();

    // For the generated globals' setters: the side object, allocated on first write.
    internal GlobalAttrs GlobalAttrsForWrite => Globals;

    // Each setter mirrors the Role/Aria shape: assigning null to an element that never set one is a
    // no-op, so neither the LiveState nor the side object is forced into existence by a null write.
    internal string? LangInternal
    {
        get => _live?.Globals?.Lang;
        set
        {
            if (value is not null || _live?.Globals is not null)
            {
                Globals.Lang = value;
            }
        }
    }

    internal string? DirInternal
    {
        get => _live?.Globals?.Dir;
        set
        {
            if (value is not null || _live?.Globals is not null)
            {
                Globals.Dir = value;
            }
        }
    }

    internal string? PopoverInternal
    {
        get => _live?.Globals?.Popover;
        set
        {
            if (value is not null || _live?.Globals is not null)
            {
                Globals.Popover = value;
            }
        }
    }

    internal string? ContentEditableInternal
    {
        get => _live?.Globals?.ContentEditable;
        set
        {
            if (value is not null || _live?.Globals is not null)
            {
                Globals.ContentEditable = value;
            }
        }
    }

    internal bool? SpellcheckInternal
    {
        get => _live?.Globals?.Spellcheck;
        set
        {
            if (value is not null || _live?.Globals is not null)
            {
                Globals.Spellcheck = value;
            }
        }
    }

    internal bool? TranslateInternal
    {
        get => _live?.Globals?.Translate;
        set
        {
            if (value is not null || _live?.Globals is not null)
            {
                Globals.Translate = value;
            }
        }
    }

    // Emit one attribute with the standard space prefix. Null value → bare attribute
    // (e.g. `required`, `disabled`); non-null → name="encoded-value" with full HTML escaping
    // matching the prior HtmlSerializer behaviour. Fast-paths plain ASCII values through
    // HtmlSerializer.AppendEncoded so encoder-no-op cases skip the allocation.
    protected static void AppendAttr(StringBuilder sb, string name, string? value)
    {
        sb.Append(' ').Append(name);
        if (value is not null)
        {
            sb.Append("=\"");
            HtmlSerializer.AppendEncoded(sb, value);
            sb.Append('"');
        }

        FrameSinkScope.Current?.Attribute(name, value);
    }

    // Overload that writes a two-part attribute name directly without allocating an
    // intermediate concatenation. Used by Element for `data-{key}` — `"data-" + kv.Key`
    // would otherwise allocate a string per data-attribute per render.
    protected static void AppendAttr(StringBuilder sb, string namePrefix, string nameSuffix, string? value)
    {
        sb.Append(' ').Append(namePrefix).Append(nameSuffix);
        if (value is not null)
        {
            sb.Append("=\"");
            HtmlSerializer.AppendEncoded(sb, value);
            sb.Append('"');
        }

        if (FrameSinkScope.Current is { } fw)
        {
            // Only allocate the concatenated name when a frame writer is active. The
            // common no-frames path stays zero-allocation.
            fw.Attribute(namePrefix + nameSuffix, value);
        }
    }

    // Integer-valued attribute (e.g. tabindex). Formats the value straight into the builder via
    // a stack buffer, so the no-frames render path allocates nothing — int.ToString() would
    // allocate a string per element on every render. An int's text is always HTML-safe (digits
    // plus an optional leading minus), so it skips the encode pass.
    protected static void AppendAttr(StringBuilder sb, string name, int value)
    {
        sb.Append(' ').Append(name).Append("=\"");
        Span<char> buffer = stackalloc char[12]; // int.MinValue is 11 chars; 12 always fits.
        _ = value.TryFormat(buffer, out var written, provider: CultureInfo.InvariantCulture);
        sb.Append(buffer[..written]);
        sb.Append('"');

        if (FrameSinkScope.Current is { } fw)
        {
            // Allocate the value string only when a frame writer is active.
            fw.Attribute(name, value.ToString(CultureInfo.InvariantCulture));
        }
    }

    // URL-bearing attribute (href/cite/action and iframe/script/object sources). Scheme is
    // sanitized by default — javascript:/vbscript:/data: are neutralized to about:blank — to
    // close the DOM-XSS hole that plain HTML-encoding leaves open. Wrap a trusted value in
    // RaskUrl.Trusted(...) to opt out. Otherwise identical to AppendAttr (incl. frame sink).
    protected static void AppendUrlAttr(StringBuilder sb, string name, string? value)
    {
        if (value is not null && value.StartsWith(RaskUrl.TrustedPrefix, StringComparison.Ordinal))
        {
            AppendTrustedUrlAttr(sb, name, value);
            return;
        }

        AppendAttr(sb, name, UrlSanitizer.Sanitize(value));
    }

    // A trusted URL is written from behind its marker in place; the unmarked string is cut only for a
    // frame writer, which keeps the value to diff against.
    private static void AppendTrustedUrlAttr(StringBuilder sb, string name, string value)
    {
        var url = value.AsSpan(RaskUrl.TrustedPrefix.Length);
        sb.Append(' ').Append(name).Append("=\"");
        HtmlSerializer.AppendEncoded(sb, url);
        sb.Append('"');

        FrameSinkScope.Current?.Attribute(name, url.ToString());
    }

    // Media URL attribute (img/audio/video/source src, poster). As AppendUrlAttr but also
    // allows data:image/*, data:video/*, data:audio/* (inline media is common and inert here).
    protected static void AppendMediaUrlAttr(StringBuilder sb, string name, string? value)
    {
        if (value is not null && value.StartsWith(RaskUrl.TrustedPrefix, StringComparison.Ordinal))
        {
            AppendTrustedUrlAttr(sb, name, value);
            return;
        }

        AppendAttr(sb, name, UrlSanitizer.SanitizeMedia(value));
    }
}
