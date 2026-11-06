#!/usr/bin/env python3
"""Build src/Rask.Core/Dom/mdn.snapshot.json from MDN's own data: @webref/elements (which interface each tag uses),
@webref/idl (the WebIDL MDN's pages are written from) and @mdn/browser-compat-data (what exists, what is deprecated,
what ships), UI Events' key and code tables, the WAI-ARIA roles and aria-* attributes from the spec's own source
(aria.py), and every CSS property with its value grammar from @webref/css (css.py).

The snapshot is pure MDN data. Mapping it to C# (names, types, aliases) is the generator's job.

Each npm package is its registry tarball, checked against the registry's integrity and read in memory: nothing is
installed and no package script runs. Versions default to each package's `latest` dist-tag (its stable release),
webref's `curated` head and w3c/aria's `main` head. Each can be pinned:
    RASK_MDN_BCD, RASK_MDN_IDL, RASK_MDN_ELEMENTS, RASK_MDN_EVENTS, RASK_MDN_CSS   npm versions (stable releases)
    RASK_MDN_WEBREF                                webref commit for the spec's attribute index
    RASK_MDN_UIEVENTS_KEY, RASK_MDN_UIEVENTS_CODE  w3c/uievents-key and -code gh-pages commits (KeyboardEvent values)
    RASK_MDN_ARIA                                  w3c/aria commit for the ARIA roles and attributes

Usage:  scripts/mdn/refresh.py [snapshot]   (scripts/mdn/follow.sh runs it; the snapshot defaults to Rask.Core's)
Exit:   0 written, or upstream did not answer (nothing written) · 1 the data changed shape
"""
import copy
import json
import os
import re
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "upstream"))

import npm  # noqa: E402
import webidl  # noqa: E402
from aria import read_aria  # noqa: E402
from css import read_css  # noqa: E402
from js import UNDEFINED, collate, either, number, stringify, truthy  # noqa: E402
from upstream import ROOT, ShapeChanged, fetch, fetch_json, follow  # noqa: E402

SNAPSHOT = ROOT / "src/Rask.Core/Dom/mdn.snapshot.json"
PACKAGES = {"@mdn/browser-compat-data": "RASK_MDN_BCD", "@webref/css": "RASK_MDN_CSS", "@webref/elements": "RASK_MDN_ELEMENTS",
            "@webref/events": "RASK_MDN_EVENTS", "@webref/idl": "RASK_MDN_IDL"}
GITHUB = "application/vnd.github+json"

# An element, attribute or element event ships when two of the three engines have it unflagged, on desktop or
# mobile: `<input capture>` exists only on phones, and that is where it matters. A web API (an interface, member,
# constructor, static or its events) ships in ONE: WebUSB is Chromium's alone, and its doc comment says so, with an
# IsSupported check to guard it. Markup stays cross-engine; a call an app chooses to make need not.
ENGINES = {"chrome": ["chrome", "chrome_android"], "firefox": ["firefox", "firefox_android"], "safari": ["safari", "safari_ios"]}
# IDL types a content attribute can reflect directly.
PLAIN = {"DOMString", "USVString", "CSSOMString", "DOMString?", "boolean", "long", "unsigned long", "double", "unrestricted double"}
# https://html.spec.whatwg.org/multipage/syntax.html#void-elements
VOID = ["area", "base", "br", "col", "embed", "hr", "img", "input", "link", "meta", "source", "track", "wbr"]
# Specs whose elements are HTML or SVG. MathML is out of scope.
SPECS = {"html": "html", "html-ruby-extensions": "html", "SVG2": "svg", "filter-effects-1": "svg", "css-masking-1": "svg",
         "svg-animations": "svg"}
# What BCD files as on no standards track is left out, except these web APIs, named one by one: each is what a real
# app needs and nothing standard does instead. Its interface, members and events ship as any other, and their doc
# comments say "Non-standard.".
#   BeforeInstallPromptEvent  a PWA's own install button (Chromium): keep the event, call prompt() in a click later.
NON_STANDARD = ["BeforeInstallPromptEvent"]
# SVG Animations files each attribute group under the first element it names (`dur` for animate); its sections say
# which elements a group is for: target and timing for every animation element, values and addition for all but set.
_ANIMATION = ["animate", "animateMotion", "animateTransform", "set"]
SMIL = {"TargetElement": _ANIMATION, "TargetAttributes": _ANIMATION, "TimingAttributes": _ANIMATION,
        "ValueAttributes": _ANIMATION[:3], "AdditionAttributes": _ANIMATION[:3], "AnimateMotionElement": ["animateMotion"]}
ELEMENTISH = re.compile(r"^(Element|HTMLElement|SVGElement|GlobalEventHandlers|HTML[A-Za-z0-9_]*Element)$")


def first(items, test):
    return next((x for x in items if test(x)), None)


def ordered(items):
    """A JavaScript Set: unique, in the order first added."""
    return dict.fromkeys(items)


class Merged:
    """An interface, mixin or dictionary with its partials folded in."""

    def __init__(self, definition):
        self.name, self.inheritance = definition.name, definition.inheritance
        self.members, self.extAttrs = list(definition.members), definition.extAttrs


class Refresh:
    def __init__(self, data, heads):
        self.bcd, self.heads = data["bcd"], heads
        self.webref_events = data["events"]
        self.admitted = set()
        for name in NON_STANDARD:
            self.admit(self.bcd["api"].get(name))
        self.read_spec_index(data["dfns"])
        self.read_idl(data["idl"])
        self.elements_package, self.css_package = data["elements"], data["css"]

    # ---- BCD: what ships ---------------------------------------------------------------------------
    def admit(self, node):
        if isinstance(node, dict):
            if truthy(node.get("__compat")):
                self.admitted.add(id(node["__compat"]))
            for key, child in node.items():
                if key != "__compat":
                    self.admit(child)
        elif isinstance(node, list):
            for child in node:
                self.admit(child)

    @staticmethod
    def supports(compat, browser):
        entry = (compat.get("support") or {}).get(browser) if compat else None
        return [e for e in (entry if isinstance(entry, list) else [entry]) if truthy(e)]

    @staticmethod
    def released(e):
        return (isinstance(e.get("version_added"), str) and e["version_added"] != "preview" and not truthy(e.get("version_removed"))
                and not truthy(e.get("flags")) and not truthy(e.get("prefix")) and not truthy(e.get("alternative_name")))

    def ships(self, compat, engines=2):
        if not truthy(compat):
            return False
        s = compat.get("status") or {}
        if truthy(s.get("deprecated")) or (s.get("standard_track") is False and id(compat) not in self.admitted):
            return False
        found = sum(1 for browsers in ENGINES.values() if any(
            any(self.released(e) and not truthy(e.get("partial_implementation")) for e in self.supports(compat, b)) for b in browsers))
        return found >= engines

    def ships_anywhere(self, compat):
        return self.ships(compat, 1)

    def support(self, compat):
        """The first unflagged, unprefixed version each engine shipped it in, keyed by the browser that ships it: the
        desktop one, or the mobile one where only that does. The doc comment's support line and the targets
        analyzer's table; runtime checks never read it, they detect the feature itself."""
        out = {}
        for browsers in ENGINES.values():
            for browser in browsers:
                e = first(self.supports(compat, browser), self.released)
                if e:
                    out[browser] = re.sub(r"^≤", "", e["version_added"])
                    break
        return out

    def meta(self, compat):
        status = (compat.get("status") or {}) if compat else {}
        spec = compat.get("spec_url", UNDEFINED) if compat else UNDEFINED
        if isinstance(spec, list):
            spec = spec[0] if spec else UNDEFINED
        return {
            "experimental": True if status.get("experimental") is True else UNDEFINED,
            "nonStandard": True if compat and id(compat) in self.admitted and status.get("standard_track") is False else UNDEFINED,
            "support": self.support(compat),
            "mdn": compat.get("mdn_url", UNDEFINED) if compat else UNDEFINED,
            "spec": spec,
        }

    def api(self, *path):
        node = self.bcd["api"]
        for key in path:
            node = node.get(key) if isinstance(node, dict) else None
        return node

    def compat_of(self, *path):
        node = self.api(*path)
        return node.get("__compat") if isinstance(node, dict) else None

    # ---- the spec's own index of content attributes per element -------------------------------------
    def read_spec_index(self, dfns):
        """webref publishes it from the specs MDN is written from; not on npm, so it comes from webref's curated
        branch, pinned to the commit it was read at so a refresh is reproducible."""
        self.spec_attrs = {"html": {}, "svg": {}}  # tag -> ordered set of attr, conforming only
        self.spec_attr_names = set()
        self.smil_attrs = set()  # "animate.begin": what SVG Animations gives each animation element
        # "img/loading" -> its keywords, in spec order: HTML's enumerated attributes, as the spec lists their values
        # ("html-global/dir" for a global attribute). A keyword the spec names by its state ("circle state") is the one
        # its link ends in ("…-circle").
        self.keyword_sets = {}
        for file, ns in [("html", "html"), ("SVG2", "svg"), ("filter-effects-1", "svg"), ("css-masking-1", "svg"), ("svg-animations", "svg")]:
            for d in dfns[file]["dfns"]:
                heading = (d.get("heading") or {}).get("id")
                if d["type"] == "attr-value" and file == "html":
                    keyword = d["href"][d["href"].rfind("-") + 1:] if re.search(r"\s", d["linkingText"][0]) else d["linkingText"][0]
                    for key in d["for"]:
                        values = self.keyword_sets.setdefault(key, [])
                        if keyword not in values:
                            values.append(keyword)
                    continue
                if d["type"] != "element-attr" or heading == "non-conforming-features":
                    continue
                for tag in (file == "svg-animations" and SMIL.get(heading)) or d["for"]:
                    if file == "svg-animations":
                        self.smil_attrs.update(f"{tag}.{name}" for name in d["linkingText"])
                    names = self.spec_attrs[ns].setdefault(tag, {})
                    for name in d["linkingText"]:
                        names[name] = None
                        self.spec_attr_names.add(name.lower())

    # ---- IDL: merge partials and mixins -------------------------------------------------------------
    def read_idl(self, files):
        self.interfaces, self.mixins, self.enums, self.dictionaries, self.callbacks, self.typedefs = {}, {}, {}, {}, {}, {}
        # The members a [SecureContext] interface, partial or mixin declares: they exist only on an HTTPS (or localhost) page.
        self.secured_by_definition = set()
        includes = []

        def merge(into, d):
            if any(a.name == "SecureContext" for a in d.extAttrs):
                self.secured_by_definition.update(id(m) for m in d.members)
            current = into.get(d.name)
            if current is None:
                into[d.name] = Merged(d)
            else:
                current.members.extend(d.members)
                if not d.partial and d.inheritance:
                    current.inheritance = d.inheritance
                if not d.partial and d.extAttrs:
                    current.extAttrs = d.extAttrs

        for text in files:
            for d in webidl.parse(text):
                if d.type == "interface":
                    merge(self.interfaces, d)
                elif d.type == "interface mixin":
                    merge(self.mixins, d)
                elif d.type == "includes":
                    includes.append(d)
                elif d.type == "enum":
                    self.enums[d.name] = d.values
                elif d.type == "dictionary":
                    merge(self.dictionaries, d)
                elif d.type == "callback":
                    self.callbacks[d.name] = d
                elif d.type == "typedef":
                    self.typedefs[d.name] = d.idlType
        for inc in includes:
            target, mixin = self.interfaces.get(inc.target), self.mixins.get(inc.includes)
            if target and mixin:
                for m in mixin.members:
                    copied = copy.copy(m)
                    copied.mixin, copied.origin = inc.includes, m
                    target.members.append(copied)

    def type_of(self, t):
        if t is None or isinstance(t, list):
            return "undefined"  # an iterable's pair of types reads as no one type
        if t.union:
            s = "(" + " or ".join(self.type_of(x) for x in t.idlType) + ")"
        elif t.generic:
            s = f"{t.generic}<{', '.join(self.type_of(x) for x in t.idlType)}>"
        # A typedef is spelled out (navigator.vibrate's VibratePattern is `(unsigned long or sequence<unsigned long>)`),
        # so what it stands for maps like any other type.
        elif t.idlType in self.typedefs:
            s = self.type_of(self.typedefs[t.idlType])
            if t.nullable:
                s = re.sub(r"\?$", "", s)
        else:
            s = t.idlType
        return s + ("?" if t.nullable else "")

    @staticmethod
    def ext(m, name):
        return first(m.extAttrs, lambda a: a.name == name)

    def secured(self, m):
        return (self.ext(m, "SecureContext") is not None or id(m) in self.secured_by_definition
                or id(getattr(m, "origin", None)) in self.secured_by_definition)

    @staticmethod
    def ext_value(a):
        if a is None or not a.rhs:
            return UNDEFINED
        unquote = lambda v: re.sub(r'^"(.*)"$', r"\1", v) if isinstance(v, str) else v  # noqa: E731
        value = a.rhs["value"]
        return [unquote(v) for v in value] if isinstance(value, list) else unquote(value)

    def interface_chain(self, name):
        while name:
            yield name
            name = self.interfaces[name].inheritance if name in self.interfaces else None

    def is_event(self, name):
        return "Event" in self.interface_chain(name)

    def args(self, m):
        return [{"name": a.name, "type": self.type_of(a.idlType), "optional": either(a.optional), "variadic": either(a.variadic)}
                for a in m.arguments]

    # ---- keywords -----------------------------------------------------------------------------------
    # An HTML attribute's closed set of keywords, each with BCD's support where BCD files the keyword on its own
    # (`popover="hint"`). The IDL enum the reflecting attribute is named after, where there is one (`referrerPolicy` →
    # ReferrerPolicy); else the spec's, where every tag that carries the attribute lists the same values. A form-submission
    # override (`formmethod`) has the form's own attribute's (`method`). A set is open, and recorded as none, where it names
    # a prefix (autocomplete's "section-") or BCD files a keyword it lacks (meta's name="viewport", from another spec).
    def idl_enum_of(self, prop):
        if not truthy(prop):
            return None
        name = prop[0].upper() + prop[1:]
        return name if name in self.enums else None

    def from_enum(self, name):
        return {"enum": name, "keywords": [{"value": v} for v in self.enums[name]]}

    def keyword(self, value, compat):
        return {"value": value, **(self.meta(compat) if compat else {})}

    @staticmethod
    def closed(values, filed):
        return not any(v.endswith("-") for v in values) and all(k == "__compat" or k in values for k in filed)

    def element_attr(self, tag, attr):
        node = self.bcd["html"]["elements"].get(tag)
        return node.get(attr) if isinstance(node, dict) else None

    def keywords_of(self, tags, attr, prop):
        if self.idl_enum_of(prop):
            return self.from_enum(self.idl_enum_of(prop))
        sets = [self.keyword_sets.get(f"{tag}/{attr}") or (self.keyword_sets.get(f"form/{attr[4:]}") if attr.startswith("form") else None)
                for tag in tags]
        if not sets[0] or any(not s or " ".join(s) != " ".join(sets[0]) for s in sets):
            return {}
        if not all(self.closed(sets[0], list(self.element_attr(t, attr) or {})) for t in tags):
            return {}

        def compat(value):
            found = (((self.element_attr(t, attr) or {}).get(value) or {}).get("__compat") for t in tags)
            return first(found, truthy)

        return {"keywords": [self.keyword(value, compat(value)) for value in sets[0]]}

    def global_keywords_of(self, attr, prop):
        if self.idl_enum_of(prop):
            return self.from_enum(self.idl_enum_of(prop))
        values = self.keyword_sets.get(f"html-global/{attr}")
        filed = self.bcd["html"]["global_attributes"].get(attr) or {}
        if not values or not self.closed(values, list(filed)):
            return {}
        return {"keywords": [self.keyword(value, (filed.get(value) or {}).get("__compat")) for value in values]}

    # ---- elements, global attributes, events --------------------------------------------------------
    def read_elements(self):
        self.elements = []
        for spec, ns in SPECS.items():
            for e in (self.elements_package.get(spec) or {}).get("elements") or []:
                if not truthy(e.get("interface")) or truthy(e.get("obsolete")):
                    continue
                compat = ((self.bcd[ns]["elements"].get(e["name"])) or {}).get("__compat")
                if not self.ships(compat):
                    continue
                if any(x["tag"] == e["name"] and x["namespace"] == ns for x in self.elements):
                    continue
                self.elements.append({"tag": e["name"], "namespace": ns, "interface": e["interface"],
                                      "void": True if e["name"] in VOID else UNDEFINED, **self.meta(compat)})
        self.elements.sort(key=lambda x: (collate(x["namespace"]), collate(x["tag"])))

        self.globals = {}
        for ns in ["html", "svg"]:
            self.globals[ns] = sorted(name for name, d in (self.bcd[ns].get("global_attributes") or {}).items()
                                      if name != "data_attributes" and self.ships(d.get("__compat")))

    def read_events(self):
        """Events every element can fire: GlobalEventHandlers' EventHandler attributes. The IDL names them;
        @webref/events says which interface each is dispatched with and whether it bubbles; BCD says whether it ships,
        on an element (an event only a Window or Document fires is not an element's)."""
        handlers = self.mixins["GlobalEventHandlers"].members if "GlobalEventHandlers" in self.mixins else []
        types = [m.name[2:] for m in handlers
                 if m.type == "attribute" and (m.name or "").startswith("on") and "EventHandler" in self.type_of(m.idlType)]
        # …and the element events that have no on-attribute (focusin, compositionend, fullscreenchange), which BCD files
        # on Element/HTMLElement: MDN documents them as element events all the same.
        for i in ["Element", "HTMLElement"]:
            for k in sorted(self.bcd["api"].get(i) or {}):
                if k.endswith("_event") and k[:-6] not in types:
                    types.append(k[:-6])
        self.events = []
        for type in types:
            # The first element interface on which it SHIPS: cancel is filed on HTMLInputElement (a file picker's, newer)
            # before HTMLDialogElement (a dialog's, everywhere).
            on = first(self.bcd["api"], lambda i: ELEMENTISH.match(i) and self.ships(self.compat_of(i, f"{type}_event")))
            if not on:
                continue
            compat = self.compat_of(on, f"{type}_event")
            dispatched = [e for e in self.webref_events if e.get("type") == type]
            on_element = [e for e in dispatched if any(ELEMENTISH.match(t["target"]) for t in e.get("targets") or [])]
            # Another spec's event of the same name (a stream's cancel) must not lend it its interface or bubbling, so the
            # entry dispatched at the interface it ships on wins, then any element's.
            at_on = [e for e in dispatched if any(t["target"] == on for t in e.get("targets") or [])]
            pick = at_on or on_element or dispatched
            iface = first((e.get("interface") for e in pick), lambda i: truthy(i) and i != "Event" and self.is_event(i)) or "Event"
            targets = [t for e in pick for t in e.get("targets") or [] if ELEMENTISH.match(t["target"])]
            self.events.append({
                "type": type, "interface": iface,
                "bubbles": True if any(t.get("bubbles") is True for t in targets) else UNDEFINED,
                "cancelable": True if any(e.get("cancelable") is True for e in pick) else UNDEFINED,
                "on": on, **self.meta(compat),
            })

    # ---- interfaces ---------------------------------------------------------------------------------
    def wanted(self):
        """Every element interface plus its ancestors, every event interface and what it holds, and the value objects
        an element member hands back."""
        wanted = ordered(n for e in self.elements for n in self.interface_chain(e["interface"]))
        # …every event interface and its ancestors, and the interfaces an event's members hold (TouchEvent.touches is a
        # TouchList of Touch; ClipboardEvent.clipboardData a DataTransfer), one level deep.
        event_interfaces = ordered(n for e in self.events for n in self.interface_chain(e["interface"]))
        for n in list(event_interfaces):
            for m in self.interfaces[n].members if n in self.interfaces else []:
                if m.type != "attribute" or not m.name:
                    continue
                held = re.sub(r"\?$", "", self.type_of(m.idlType))
                if held not in self.interfaces or held in ("EventTarget", "Window"):
                    continue
                event_interfaces[held] = None
                item = first(self.interfaces[held].members, lambda x: x.type == "operation" and x.name == "item")
                item_type = re.sub(r"\?$", "", self.type_of(item.idlType)) if item else None
                if item_type and item_type in self.interfaces:
                    event_interfaces[item_type] = None
        wanted.update(event_interfaces)

        # …and the value objects an element member hands back that serialize themselves (getBoundingClientRect's DOMRect,
        # by its toJSON), with their ancestors: a typed ref returns them as records.
        def to_json(name):
            return any(any(m.type == "operation" and m.name == "toJSON" for m in self.interfaces[n].members)
                       for n in self.interface_chain(name) if n in self.interfaces)

        for e in self.elements:
            for n in self.interface_chain(e["interface"]):
                for m in self.interfaces[n].members if n in self.interfaces else []:
                    held = re.sub(r"\?$", "", re.sub(r"^Promise<(.*)>$", r"\1", self.type_of(m.idlType))) if m.idlType else ""
                    if held in self.interfaces and to_json(held):
                        wanted.update(ordered(self.interface_chain(held)))
        return sorted(wanted)

    def members_of(self, name):
        definition, api = self.interfaces[name], self.bcd["api"].get(name) or {}
        members = []
        for m in definition.members:
            if not m.name or m.special == "static":
                continue
            if m.type not in ("attribute", "operation"):
                continue
            # BCD files the window's own globals (fetch, performance, crypto: WindowOrWorkerGlobalScope's) at the top of api.
            compat = (api.get(m.name) or {}).get("__compat")
            if compat is None and name == "Window":
                compat = self.compat_of(m.name)
            if not self.ships_anywhere(compat):
                continue
            type = self.type_of(m.idlType) if m.type == "attribute" else UNDEFINED
            if type in ("EventHandler", "EventHandler?"):
                continue  # events are Rask's own surface
            if m.type == "attribute":
                entry = {"kind": "attribute", "name": m.name, "type": type, "readonly": either(m.readonly)}
            else:
                entry = {"kind": "operation", "name": m.name, "returns": self.type_of(m.idlType), "args": self.args(m)}
            reflect = self.ext(m, "Reflect")
            if reflect:
                value = self.ext_value(reflect)
                entry["reflect"] = m.name.lower() if value is None or value is UNDEFINED else value
            default = self.ext_value(self.ext(m, "ReflectDefault"))
            if default is not UNDEFINED:
                entry["reflectDefault"] = default
            reflect_range = self.ext_value(self.ext(m, "ReflectRange"))
            if reflect_range is not UNDEFINED:
                entry["reflectRange"] = [number(v) for v in reflect_range]
            if getattr(m, "mixin", None):
                entry["mixin"] = m.mixin
            if self.secured(m):
                entry["secure"] = True
            entry.update(self.meta(compat))
            # Overloads collapse to the first one that ships; the rest are recorded as args variants.
            prior = first(members, lambda x: x["kind"] == "operation" and x["name"] == m.name)
            if prior:
                prior.setdefault("overloads", []).append(entry["args"])
                continue
            members.append(entry)
        return members

    def describe(self, name):
        definition = self.interfaces[name]
        compat = self.compat_of(name)
        tags = [e for e in self.elements if e["interface"] == name]
        members = self.members_of(name)
        # Content attributes this interface's tags carry, in IDL order (the render order). A tag's attributes are the
        # spec's conforming ones, BCD's, and the IDL attributes that reflect one; global attributes are Element's, not a
        # tag's. Where BCD knows the attribute, it has to ship.
        attributes = []
        ns = tags[0]["namespace"] if tags else UNDEFINED
        for tag in tags:
            bcd_element = self.bcd[tag["namespace"]]["elements"][tag["tag"]]
            names = ordered(self.spec_attrs[tag["namespace"]].get(tag["tag"]) or [])
            names.update(ordered(a for a in bcd_element if a != "__compat" and "_" not in a))
            names.update(ordered(self.reflected_names(name, set(names) | set(bcd_element))))
            for attr in sorted(names):
                data = bcd_element.get(attr)
                # An on* attribute is inline JavaScript (SVG's onbegin); events are Rask's own typed surface.
                if (truthy(data) and not self.ships(data.get("__compat"))) or attr in self.globals[tag["namespace"]] or re.match(r"^on[a-z]", attr):
                    continue
                known = first(attributes, lambda a: a["attr"] == attr)
                if known:
                    known["tags"].append(tag["tag"])
                    continue
                idl = self.find_reflecting(name, attr)
                # webref scopes some shared definitions too widely (form's `action` onto button); an attribute only the spec
                # index names is kept when the element's own IDL has it too. Except SMIL's: BCD barely covers the animation
                # elements (no `begin` on animate), and they reflect nothing, so their spec is enough.
                if not truthy(data) and not idl and f"{tag['tag']}.{attr}" not in self.smil_attrs:
                    continue
                field = lambda key: idl[key] if idl else UNDEFINED  # noqa: E731
                attributes.append({"attr": attr, "property": field("name"), "type": field("type"), "url": field("url"),
                                   "readonly": field("readonly"), "reflect": field("reflect"), "reflectDefault": field("reflectDefault"),
                                   "reflectRange": field("reflectRange"), "on": field("on"), "tags": [tag["tag"]],
                                   **(self.meta(data.get("__compat")) if truthy(data) else {})})

        # IDL order, in the interface that DECLARES the attribute: an inherited one (video's `src`, declared on
        # HTMLMediaElement) keeps its place there rather than falling to the end of the tag's own list.
        def order(a):
            if a["on"] is not UNDEFINED:
                names = [m.name for m in (self.interfaces[a["on"]].members if a["on"] in self.interfaces else [])]
            else:
                names = [m["name"] for m in members]
            return names.index(a["property"]) if a["property"] in names else 1e6

        attributes.sort(key=lambda a: (order(a), collate(a["attr"])))
        if ns == "html":
            for a in attributes:
                a.update(self.keywords_of(a["tags"], a["attr"], a["property"]))
        for a in attributes:
            if len(a["tags"]) == len(tags):
                del a["tags"]  # on every tag of the interface
        constructors, statics, events = self.extras_of(name)
        return {"parent": definition.inheritance, "abstract": True if not tags else UNDEFINED, "namespace": ns,
                "exposed": True if self.exposed_to_window(definition) else UNDEFINED,
                "secure": True if self.ext(definition, "SecureContext") else UNDEFINED, **self.meta(compat),
                "attributes": attributes or UNDEFINED, "members": members, "constructors": constructors or UNDEFINED,
                "statics": statics or UNDEFINED, "events": events or UNDEFINED}

    def extras_of(self, name):
        """What `new X(…)`, `X.staticMember` and `x.onfoo` offer, apart from the instance members the elements' code
        reads: a constructor per argument list, the static members that ship, and the events an EventHandler attribute names."""
        definition, api = self.interfaces[name], self.bcd["api"].get(name) or {}
        constructors, statics, events = [], [], []
        for m in definition.members:
            if m.type == "constructor":
                compat = (api.get(name) or {}).get("__compat")
                if self.ships_anywhere(compat):
                    constructors.append({"args": self.args(m), "secure": True if self.ext(definition, "SecureContext") else UNDEFINED,
                                         **self.meta(compat)})
                continue
            # BCD files a static member as name_static, beside any instance member of the same name.
            node = api.get(m.name + "_static") if m.name else None
            if node is None and m.name:
                node = api.get(m.name)
            compat = (node or {}).get("__compat")
            if m.special == "static" and m.name and self.ships_anywhere(compat):
                prior = first(statics, lambda x: x["kind"] == "operation" and x["name"] == m.name)
                if prior:
                    prior.setdefault("overloads", []).append(self.args(m))
                    continue
                secure = True if self.secured(m) else UNDEFINED
                if m.type == "attribute":
                    statics.append({"kind": "attribute", "name": m.name, "type": self.type_of(m.idlType), "readonly": either(m.readonly),
                                    "secure": secure, **self.meta(compat)})
                else:
                    statics.append({"kind": "operation", "name": m.name, "returns": self.type_of(m.idlType), "args": self.args(m),
                                    "secure": secure, **self.meta(compat)})
                continue
            if m.type == "attribute" and (m.name or "").startswith("on") and self.type_of(m.idlType).startswith("EventHandler"):
                type = m.name[2:]
                if self.ships_anywhere((api.get(f"{type}_event") or {}).get("__compat")):
                    events.append({"type": type, "interface": self.event_interface(name, type)})
        return constructors, statics, events

    def reflected_names(self, iface, known):
        """The content attributes an interface's own IDL reflects: [Reflect], or a writable, plainly typed attribute
        named after an attribute the spec defines (input's `width` reflects `width`; `valueAsNumber` reflects none).
        SVG reflects every attribute as a read-only SVGAnimated* object named after it (`pathLength`, `viewBox`), after
        one axis of it (`stdDeviationX` for `stdDeviation`, where the tag knows no `stdDeviationX`), or as `in1`, since
        `in` is a JavaScript keyword. `known` is what the spec index and BCD name for the tag."""
        out = []
        n = iface
        while n and n not in ("HTMLElement", "SVGElement", "Element"):
            for m in self.interfaces[n].members if n in self.interfaces else []:
                if m.type != "attribute" or not m.name or m.special == "static" or not self.ships(self.compat_of(n, m.name)):
                    continue
                r = self.ext(m, "Reflect")
                type = self.type_of(m.idlType)
                if r:
                    value = self.ext_value(r)
                    out.append(m.name.lower() if value is None or value is UNDEFINED else value)
                elif not m.readonly and type in PLAIN and m.name.lower() in self.spec_attr_names:
                    out.append(m.name.lower())
                elif m.readonly and type.startswith("SVGAnimated"):
                    stem = re.sub(r"(X|Y|Type|Angle)$", "", m.name)
                    out.append("in" if m.name == "in1" else stem if m.name not in known and stem in known else m.name)
            n = self.interfaces[n].inheritance if n in self.interfaces else None
        return out

    def find_reflecting(self, iface, attr):
        """The IDL attribute that reflects a content attribute, searched up the interface chain."""
        flat = attr.replace("-", "").lower()
        n = iface
        while n:
            definition = self.interfaces.get(n)
            if not definition:
                break

            def reflecting(x):
                r = self.ext(x, "Reflect")
                if not r:
                    return False
                value = self.ext_value(r)
                return (x.name.lower() if value is None or value is UNDEFINED else value) == attr

            plain = [x for x in definition.members if x.type == "attribute" and x.name and self.type_of(x.idlType) in PLAIN]
            # The member named after the attribute wins (`checked`, not `defaultChecked`, sets the checked attribute from
            # markup); otherwise the one whose [Reflect] names it (`colSpan` for colspan).
            m = (first(plain, lambda x: x.name.lower() == flat) or first(plain, reflecting)
                 or first(definition.members, lambda x: x.type == "attribute" and x.name and reflecting(x))
                 or first(definition.members, lambda x: x.type == "attribute" and x.name and x.name.lower() == flat))
            if m:
                type = self.type_of(m.idlType)
                reflect_range = self.ext_value(self.ext(m, "ReflectRange"))
                # A URL attribute: [ReflectURL], or typed USVString, which the HTML IDL keeps for URLs.
                return {"name": m.name, "type": type,
                        "url": True if self.ext(m, "ReflectURL") or type.startswith("USVString") else UNDEFINED,
                        "readonly": either(m.readonly), "reflect": True if self.ext(m, "Reflect") else UNDEFINED,
                        "reflectDefault": self.ext_value(self.ext(m, "ReflectDefault")),
                        "reflectRange": UNDEFINED if reflect_range is UNDEFINED or reflect_range is None else [number(v) for v in reflect_range],
                        "on": UNDEFINED if n == iface else n}
            n = definition.inheritance
        return None

    def exposed_to_window(self, definition):
        """Rask.Web generates MDN's globals (Navigator, Window, Document) and every interface they reach from these."""
        exposed = self.ext(definition, "Exposed")
        # [Exposed=*] parses as rhs { type: "*", value: null }: everywhere, the window included.
        if exposed and exposed.rhs and exposed.rhs["type"] == "*":
            return True
        value = self.ext_value(exposed)
        return value == "*" or value == "Window" or (isinstance(value, list) and "Window" in value)

    def event_interface(self, target, type):
        """The interface an event of `type` is dispatched with at `target` (or an ancestor of it): @webref/events says,
        and a plain Event where it says nothing."""
        chain = list(self.interface_chain(target))
        entry = first(self.webref_events, lambda e: e.get("type") == type and any(t["target"] in chain for t in e.get("targets") or []))
        return entry["interface"] if entry and truthy(entry.get("interface")) and self.is_event(entry["interface"]) else "Event"

    # ---- enums, dictionaries and callbacks the members reach (for typed refs and Rask.Web) -----------
    def reach_all(self, interfaces):
        self.reached_enums, self.reached_dicts, self.reached_callbacks = {}, {}, {}
        for i in interfaces.values():
            for m in [*i["members"], *([] if i["statics"] is UNDEFINED else i["statics"]),
                      *([] if i["constructors"] is UNDEFINED else i["constructors"])]:
                held = m.get("type", UNDEFINED)
                if held is UNDEFINED or held is None:
                    held = m.get("returns", UNDEFINED)
                if truthy(held):
                    self.reach(held)
                for a in m.get("args") or []:
                    self.reach(a["type"])
                # The overloads' options too: LockManager.request's (ifAvailable, steal) are only in its second argument list.
                for a in (a for overload in m.get("overloads") or [] for a in overload):
                    self.reach(a["type"], False)
        # The dictionaries an `object` argument really takes, which the IDL never names (Permissions.query's descriptor):
        # the generator's table in src/Rask.Dom.Tasks/WebObjectArgs.cs. Keep the two lists together.
        for d in ["PermissionDescriptor"]:
            self.reach(d)
        # …and the IDL enum an attribute's keywords are (referrerpolicy's ReferrerPolicy).
        for i in interfaces.values():
            for a in [] if i["attributes"] is UNDEFINED else i["attributes"]:
                if truthy(a.get("enum")):
                    self.reach(a["enum"])

    def reach(self, type, with_callbacks=True):
        """`with_callbacks`: False for an overload's arguments, whose callbacks are legacy forms that the promise the
        main one answers with replaced (RTCPeerConnection's success and failure callbacks)."""
        for n in re.findall(r"[A-Za-z_][A-Za-z0-9_]*", type):
            if n in self.enums and n not in self.reached_enums:
                self.reached_enums[n] = self.enums[n]
            c = self.callbacks.get(n) if with_callbacks else None
            if c and n not in self.reached_callbacks:
                self.reached_callbacks[n] = {"returns": self.type_of(c.idlType), "args": self.args(c)}
                self.reach(self.reached_callbacks[n]["returns"])
                for a in self.reached_callbacks[n]["args"]:
                    self.reach(a["type"])
            d = self.dictionaries.get(n)
            if d and n not in self.reached_dicts:
                self.reached_dicts[n] = {"parent": d.inheritance if d.inheritance is not None else UNDEFINED,
                                         "members": [{"name": m.name, "type": self.type_of(m.idlType), "required": either(m.required)}
                                                     for m in d.members if m.name]}
                if d.inheritance:
                    self.reach(d.inheritance, with_callbacks)
                for m in self.reached_dicts[n]["members"]:
                    self.reach(m["type"], with_callbacks)

    # ---- the snapshot -------------------------------------------------------------------------------
    def snapshot(self, versions, keys, codes, aria):
        self.read_elements()
        self.read_events()
        interfaces = {name: self.describe(name) for name in self.wanted() if name in self.interfaces}
        # The web platform: every interface a window exposes that ships, and its ancestors.
        web = []
        for name, definition in self.interfaces.items():
            if not self.exposed_to_window(definition) or not self.ships_anywhere(self.compat_of(name)):
                continue
            web.append(name)
            for n in self.interface_chain(name):
                if n in interfaces:
                    break
                interfaces[n] = self.describe(n)
        self.reach_all(interfaces)
        by_name = lambda o: dict(sorted(o.items(), key=lambda kv: collate(kv[0])))  # noqa: E731

        def global_attribute(ns, attr):
            idl = self.find_reflecting("HTMLElement" if ns == "html" else "SVGElement", attr)
            field = lambda key: idl[key] if idl else UNDEFINED  # noqa: E731
            compat = (self.bcd[ns]["global_attributes"].get(attr) or {}).get("__compat")
            return {"attr": attr, "property": field("name"), "type": field("type"), "url": field("url"), "reflect": field("reflect"),
                    "on": field("on"), **self.meta(compat), **(self.global_keywords_of(attr, field("name")) if ns == "html" else {})}

        return {
            "schema": 1,
            "sources": {**versions, "webref/dfns": self.heads["webref"], "w3c/uievents-code": self.heads["code"],
                        "w3c/uievents-key": self.heads["key"], "w3c/aria": self.heads["aria"]},
            "engines": list(ENGINES),
            "elements": self.elements,
            # Each global attribute with the IDL attribute that reflects it, searched from HTMLElement / SVGElement up.
            "globalAttributes": {ns: [global_attribute(ns, attr) for attr in names] for ns, names in self.globals.items()},
            "events": self.events,
            "web": sorted(web),
            "interfaces": by_name(interfaces),
            "enums": by_name(self.reached_enums),
            "dictionaries": by_name(self.reached_dicts),
            "callbacks": by_name(self.reached_callbacks),
            # KeyboardEvent.key's named values and KeyboardEvent.code's values, each with its table and spec anchor.
            "keys": keys,
            "codes": codes,
            "aria": aria,
            # Every CSS property two engines ship, with the keywords and value types that are a whole value alone.
            "css": read_css(self.css_package, self.bcd, self.ships, self.meta),
        }


def keyboard_values(kind, sha, webref):
    """KeyboardEvent key and code values. UI Events keeps them in two specs of their own (uievents-key, uievents-code)
    whose tables no package carries, so each is read from its gh-pages source, pinned to the commit read. Every value
    must be an anchor of the published spec, as webref's id index (the commit above) lists it: a table this parser
    misread fails the refresh instead of shipping."""
    source = fetch(f"https://raw.githubusercontent.com/w3c/uievents-{kind}/{sha}/index-source.txt").decode("utf-8")
    anchors = set(fetch_json(f"https://raw.githubusercontent.com/w3c/webref/{webref}/ed/ids/uievents-{kind}.json")["ids"])
    # `BEGIN_KEY_TABLE navigation` … `KEY ArrowDown <prose>` … `END_KEY_TABLE`. A `_OPT` row is one the spec's "Required"
    # column says No to; a `_DUP` row repeats a value another table defines, and is skipped.
    table = kind.upper()
    begin = re.compile(rf"^\s*BEGIN_{table}_TABLE\s+([A-Za-z0-9_-]+)")
    end = re.compile(rf"^\s*END_{table}_TABLE\b")
    row = re.compile(rf"^\s*{table}(_OPT)?\s+([A-Za-z0-9_]+)(?:\s|$)")
    values, group = [], None
    for line in source.split("\n"):
        if begin.match(line):
            group = begin.match(line).group(1)
        elif end.match(line):
            group = None
        elif group and row.match(line):
            optional, name = row.match(line).groups()
            spec = f"https://w3c.github.io/uievents-{kind}/#{kind}-{name}"
            if spec not in anchors:
                raise ShapeChanged(f"uievents-{kind}: {name} has no anchor in the published spec")
            if any(v["name"] == name for v in values):
                raise ShapeChanged(f"uievents-{kind}: {name} is defined twice")
            values.append({"name": name, "group": group, "optional": True if optional else UNDEFINED, "spec": spec})
    if not values:
        raise ShapeChanged(f"uievents-{kind}: no {kind} table found")
    return sorted(values, key=lambda v: collate(v["name"]))


def head(repo, branch):
    sha = fetch_json(f"https://api.github.com/repos/{repo}/commits/{branch}", accept=GITHUB).get("sha")
    if not sha:
        raise ShapeChanged(f"could not resolve {repo}'s {branch} branch")
    return sha


def packages():
    """Each package's release, and the files refresh reads out of its tarball, as text."""
    versions, contents = {}, {}
    for name, pin in PACKAGES.items():
        manifest = npm.release(name, os.environ.get(pin) or "latest")
        versions[name] = manifest["version"]
        top = re.compile(r"package/[^/]+\.(json|idl)")
        contents[name] = {path.removeprefix("package/"): data.decode("utf-8") for path, data in
                          npm.files(npm.tarball(manifest), lambda p: top.fullmatch(p) is not None and p != "package/package.json").items()}
    return versions, contents


def read(contents, heads):
    """The data each half of the snapshot reads, in the order and shape its package's own loader hands it over."""
    by_stem = lambda files, ext: {p[: -len(ext)]: json.loads(t) for p, t in sorted(files.items()) if p.endswith(ext)}  # noqa: E731
    dfns = {file: fetch_json(f"https://raw.githubusercontent.com/w3c/webref/{heads['webref']}/ed/dfns/{file}.json")
            for file in ["html", "SVG2", "filter-effects-1", "css-masking-1", "svg-animations"]}
    return {
        "bcd": json.loads(contents["@mdn/browser-compat-data"]["data.json"]),
        "css": json.loads(contents["@webref/css"]["css.json"]),
        "elements": by_stem(contents["@webref/elements"], ".json"),
        "events": json.loads(contents["@webref/events"]["events.json"]),
        "idl": [text for path, text in sorted(contents["@webref/idl"].items()) if path.endswith(".idl")],
        "dfns": dfns,
    }


def step(out):
    try:
        heads = {"webref": os.environ.get("RASK_MDN_WEBREF") or head("w3c/webref", "curated"),
                 "key": os.environ.get("RASK_MDN_UIEVENTS_KEY") or head("w3c/uievents-key", "gh-pages"),
                 "code": os.environ.get("RASK_MDN_UIEVENTS_CODE") or head("w3c/uievents-code", "gh-pages"),
                 "aria": os.environ.get("RASK_MDN_ARIA") or head("w3c/aria", "main")}
        versions, contents = packages()
        refresh = Refresh(read(contents, heads), heads)
        keys = keyboard_values("key", heads["key"], heads["webref"])
        codes = keyboard_values("code", heads["code"], heads["webref"])
        # WAI-ARIA: w3c/aria's main branch, pinned to the commit it was read at.
        aria = read_aria(fetch(f"https://raw.githubusercontent.com/w3c/aria/{heads['aria']}/index.html").decode("utf-8"), heads["aria"])
        snapshot = refresh.snapshot(versions, keys, codes, aria)
    except (ValueError, SyntaxError, KeyError) as error:
        raise ShapeChanged(f"{type(error).__name__}: {error}") from error
    out.write_text(stringify(snapshot, 1) + "\n", encoding="utf-8")
    return (f"{len(snapshot['elements'])} elements, {len(snapshot['events'])} events, {len(snapshot['interfaces'])} interfaces, "
            f"{len(keys)} keys, {len(codes)} codes, {len(aria['roles'])} ARIA roles, {len(snapshot['css']['properties'])} CSS properties -> {out}")


if __name__ == "__main__":
    target = Path(sys.argv[1]) if len(sys.argv) > 1 else SNAPSHOT
    sys.exit(follow("mdn", lambda: step(target)))
