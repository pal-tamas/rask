"""The WAI-ARIA half of the snapshot: every role and every aria-* state and property, read from the spec's own source
(w3c/aria's index.html, the editor's draft MDN's ARIA pages are written from). It is not on npm, so refresh.py pins
the commit it read. Only the characteristics and value tables are read — data, never the spec's prose.
"""
import re
from html.parser import HTMLParser

from js import UNDEFINED, collate

VALUE_TYPES = ["true_false", "true_false_undefined", "tristate", "token", "token_list", "idref", "idref_list", "integer",
               "number", "string"]
# https://html.spec.whatwg.org/multipage/syntax.html#void-elements: a start tag that is the whole element.
_VOID = {"area", "base", "br", "col", "embed", "hr", "img", "input", "link", "meta", "source", "track", "wbr"}


class Element:
    def __init__(self, name, attrs):
        self.name, self.attrs, self.children = name, attrs, []

    def attr(self, name):
        return next((value for key, value in self.attrs if key == name), None)

    def classes(self):
        return (self.attr("class") or "").split()


class _Tree(HTMLParser):
    """An element tree from the spec's markup. The spec is written with every element closed, so an end tag closes
    the nearest open element of its name, and one that matches nothing open is dropped."""

    def __init__(self):
        super().__init__(convert_charrefs=True)
        self.root = Element("#document", [])
        self.open = [self.root]

    def handle_starttag(self, tag, attrs):
        element = Element(tag, [(k, v or "") for k, v in attrs])
        self.open[-1].children.append(element)
        if tag not in _VOID:
            self.open.append(element)

    def handle_startendtag(self, tag, attrs):
        self.open[-1].children.append(Element(tag, [(k, v or "") for k, v in attrs]))

    def handle_endtag(self, tag):
        for at in range(len(self.open) - 1, 0, -1):
            if self.open[at].name == tag:
                del self.open[at:]
                return

    def handle_data(self, data):
        self.open[-1].children.append(data)


def parse(html):
    tree = _Tree()
    tree.feed(html)
    tree.close()
    return tree.root


def walk(node):
    yield node
    if isinstance(node, Element):
        for child in node.children:
            yield from walk(child)


def find(node, test):
    return next((n for n in walk(node) if n is not node and isinstance(n, Element) and test(n)), None)


def text(node):
    return re.sub(r"\s+", " ", "".join(n for n in walk(node) if isinstance(n, str))).strip()


def cell(definition, cls):
    """A characteristics table's cell, by its class: `role-parent`, `property-value`."""
    return find(definition, lambda n: n.name == "td" and cls in n.classes())


def refs(td, tags):
    """The roles (<rref>) or the states and properties (<pref>, <sref>) a cell links, once each, sorted."""
    if td is None:
        return []
    return sorted(set(text(n) for n in walk(td) if isinstance(n, Element) and n.name in tags))


def role(node, kind):
    """A role's characteristics table: what it is (abstract, deprecated), what it derives from, and which states and
    properties it requires, supports and prohibits."""
    return {
        "name": text(find(node, lambda n: n.name == "rdef") or node),
        "abstract": True if text(cell(node, "role-abstract") or node) == "True" else UNDEFINED,
        "deprecated": True if "deprecated" in kind else UNDEFINED,
        "superclass": refs(cell(node, "role-parent"), ["rref"]),
        "required": refs(cell(node, "role-required-properties"), ["pref", "sref"]),
        "supported": refs(cell(node, "role-properties"), ["pref", "sref"]),
        "prohibited": refs(cell(node, "role-disallowed"), ["pref", "sref"]),
        "nameProhibited": True if "prohibited" in text(cell(node, "role-namefrom") or node) else UNDEFINED,
        "spec": f"https://w3c.github.io/aria/#{node.attr('id')}",
    }


def attribute(node, kind):
    """A state or property: its value type, its keywords and its default. The value type is the link's TEXT: two
    string-valued properties (aria-colindextext, aria-rowindextext) link `valuetype_integer` by mistake."""
    value_cell = cell(node, "property-value") or cell(node, "state-value")
    value = re.sub(r"[ /]", "_", re.sub(r"^id reference", "idref", text(value_cell or node).lower()))
    # One row per keyword, the default marked "(default)". A row of several words is the default spelled as a
    # combination of the others (aria-relevant's "additions text"), not a keyword of its own.
    values, default = [], UNDEFINED
    for th in (n for n in walk(node) if isinstance(n, Element) and n.name == "th" and "value-name" in n.classes()):
        word = re.sub(r":$", "", text(th).replace("(default)", "", 1)).strip()
        if "(default)" in text(th):
            default = word
        if " " not in word:
            values.append(word)
    return {
        "name": node.attr("id"),
        "kind": "state" if "state" in kind else "property",
        "deprecated": True if "deprecated" in kind else UNDEFINED,
        "value": value,
        "values": values if values else UNDEFINED,
        "default": default,
        "spec": f"https://w3c.github.io/aria/#{node.attr('id')}",
    }


def read_aria(html, sha):
    roles, attributes = [], []
    for node in walk(parse(html)):
        if not isinstance(node, Element) or node.name != "div" or not node.attr("id"):
            continue
        kind = node.classes()
        if "role" in kind:
            roles.append(role(node, kind))
        elif ("property" in kind or "state" in kind) and node.attr("id").startswith("aria-"):
            attributes.append(attribute(node, kind))

    if len(roles) < 50 or len(attributes) < 40:
        raise ValueError(f"w3c/aria {sha}: read {len(roles)} roles and {len(attributes)} attributes; has the spec's markup changed?")
    for a in attributes:
        if a["value"] not in VALUE_TYPES:
            raise ValueError(f'w3c/aria {sha}: {a["name"]} has the value type "{a["value"]}", which no rule maps')
    roles.sort(key=lambda r: collate(r["name"]))
    attributes.sort(key=lambda a: collate(a["name"]))
    return {"roles": roles, "attributes": attributes}
