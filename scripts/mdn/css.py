"""The CSS half of the snapshot: every property that ships, with what its value may be, read from @webref/css (the
value grammars of the CSS specs MDN's pages are written from) and @mdn/browser-compat-data (what ships).

A property's grammar is reduced to what a typed call can say in ONE value: the keywords that are a whole value on
their own (`display: grid`, never the `flow` of `display: block flow`), and the value types that are
(`width: <length>`). Everything else a grammar allows is still CSS, and is written as text. Only names and grammars
are read — data, never a spec's prose.

A result's keywords and types are dicts used as ordered sets: the snapshot lists keywords in the order the grammar
first names them.
"""
import re

from js import UNDEFINED, collate

# Valid for every property, so said once by the emitter rather than in each property's set.
CSS_WIDE = {"inherit", "initial", "unset", "revert", "revert-layer"}
# Value types kept whole. A colour's grammar reaches 150 names, and `color: red` is not a keyword of `color`; the
# rest are defined in terms of the tokenizer, which says nothing a caller can use.
OPAQUE = {"color", "integer", "number", "string", "custom-ident", "dashed-ident"}

_TOKEN = re.compile(r"<[^>]*>|\|\||&&|\{[^}]*\}|[A-Za-z_-][A-Za-z0-9_-]*\(?|[0-9][A-Za-z0-9_.]*|\S")
_MULTIPLIER = re.compile(r"^([?*+#!]|\{.*\})$")
_STOPS = {"|", "||", "&&", "]", ")"}


def read_css(css, bcd, ships, meta):
    types = {}
    # A type defined once per scope (`<content-list>`) is read where it is not scoped, or as first given.
    for t in css["types"]:
        if t["name"] not in types or not t.get("for"):
            types[t["name"]] = t
    by_name = {p["name"]: p for p in css["properties"]}
    reduce = _reducer(types, by_name)

    properties = []
    for p in css["properties"]:
        tree = bcd["css"]["properties"].get(p["name"])
        compat = tree.get("__compat") if tree else None
        # A vendor-prefixed name and a legacy alias are another property's old spelling.
        if p["name"].startswith("-") or p.get("legacyAliasOf") or not p.get("syntax") or not ships(compat):
            continue
        value = reduce(p["syntax"])
        # A keyword BCD tracks and says has not shipped (`display: grid-lanes`) is not offered; one it does not track is.
        keywords = [k for k in value["keywords"] if k not in CSS_WIDE and (
            not (tree.get(k) or {}).get("__compat") or ships(tree[k]["__compat"]))]
        properties.append({
            "name": p["name"], "keywords": keywords, "accepts": sorted(value["types"]),
            "longhands": p.get("longhands", UNDEFINED), **meta(compat), "spec": p.get("href", UNDEFINED),
        })

    if len(properties) < 300:
        raise ValueError(f"@webref/css: read {len(properties)} properties that ship; has the data's shape changed?")
    properties.sort(key=lambda p: collate(p["name"]))
    return {"properties": properties}


def _nothing():
    return {"keywords": {}, "types": {}, "nullable": False}


def _reducer(types, properties):
    """CSS value definition syntax (https://drafts.csswg.org/css-values-4/#value-defs), reduced as it is parsed.
    A result is { keywords, types, nullable }: what is a whole value alone, and whether nothing is."""
    memo = {}

    def named(key, syntax):
        if key in memo:
            return memo[key] or _nothing()  # None while it is being read: a grammar that names itself
        memo[key] = None
        result = reduce(syntax)
        memo[key] = result
        return result

    def reference(token):
        quoted = re.match(r"^<'([^']+)'", token)
        if quoted:
            name = quoted.group(1)
            return named("'" + name, properties[name].get("syntax") or "") if name in properties else _nothing()
        name = re.match(r"^<([^\s>\[]+)", token).group(1)
        if name.endswith("()"):
            return _nothing()
        syntax = None if name in OPAQUE else (types.get(name) or {}).get("syntax")
        return named(name, syntax) if syntax else {**_nothing(), "types": {name: True}}

    def reduce(syntax):
        tokens = _TOKEN.findall(syntax)
        at = 0

        def union(parts):
            if len(parts) == 1:
                return parts[0]
            return {"keywords": {k: True for p in parts for k in p["keywords"]}, "types": {t: True for p in parts for t in p["types"]},
                    "nullable": any(p["nullable"] for p in parts)}

        def all_of(parts):
            """All of them, in order or not: one is a whole value alone only where every other may be left out."""
            if len(parts) == 1:
                return parts[0]
            alone = [p for i, p in enumerate(parts) if all(i == j or o["nullable"] for j, o in enumerate(parts))]
            return {"keywords": {k: True for p in alone for k in p["keywords"]}, "types": {t: True for p in alone for t in p["types"]},
                    "nullable": all(p["nullable"] for p in parts)}

        def series(parse, separator, join):
            nonlocal at
            parts = [parse()]
            while at < len(tokens) and tokens[at] == separator:
                at += 1
                parts.append(parse())
            return join(parts)

        def choice():
            return series(any_of, "|", union)

        def any_of():
            # `a || b`: one or more of them, so each is a value alone.
            return series(both, "||", lambda parts: {**union(parts), "nullable": False})

        def both():
            return series(sequence, "&&", all_of)

        def sequence():
            parts = []
            while at < len(tokens) and tokens[at] not in _STOPS:
                parts.append(term())
            return all_of(parts) if parts else _nothing()

        def term():
            nonlocal at
            value = atom()
            while at < len(tokens) and _MULTIPLIER.match(tokens[at]):
                m = tokens[at]
                optional = m in ("?", "*") or m.startswith("{0")
                value = {**value, "nullable": False if m == "!" else value["nullable"] or optional}
                at += 1
            return value

        def atom():
            nonlocal at
            token = tokens[at]
            at += 1
            if token == "[":
                inner = choice()
                at += 1
                return inner
            if token.startswith("<"):
                return reference(token)
            if token.endswith("("):
                choice()
                at += 1
                return _nothing()
            if re.match(r"^[A-Za-z_-]", token):
                return {**_nothing(), "keywords": {token: True}}
            return _nothing()  # a literal: `,` or `/`

        return choice()

    return reduce
