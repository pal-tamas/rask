// The WAI-ARIA half of the snapshot: every role and every aria-* state and property, read from the spec's own source
// (w3c/aria's index.html, the editor's draft MDN's ARIA pages are written from). It is not on npm, so refresh.mjs
// pins the commit it read. Only the characteristics and value tables are read — data, never the spec's prose.

const VALUE_TYPES = ["true_false", "true_false_undefined", "tristate", "token", "token_list", "idref", "idref_list", "integer", "number", "string"];

export async function readAria(parse5, sha) {
  const res = await fetch(`https://raw.githubusercontent.com/w3c/aria/${sha}/index.html`);
  if (!res.ok) throw new Error(`w3c/aria index.html: HTTP ${res.status}`);
  const doc = parse5.parse(await res.text());

  const roles = [], attributes = [];
  for (const node of walk(doc)) {
    if (node.nodeName !== "div" || !attr(node, "id")) continue;
    const kind = classes(node);
    if (kind.includes("role")) roles.push(role(node, kind));
    else if ((kind.includes("property") || kind.includes("state")) && attr(node, "id").startsWith("aria-")) attributes.push(attribute(node, kind));
  }

  if (roles.length < 50 || attributes.length < 40) {
    throw new Error(`w3c/aria ${sha}: read ${roles.length} roles and ${attributes.length} attributes; has the spec's markup changed?`);
  }
  for (const a of attributes) {
    if (!VALUE_TYPES.includes(a.value)) throw new Error(`w3c/aria ${sha}: ${a.name} has the value type "${a.value}", which no rule maps`);
  }
  roles.sort((a, b) => a.name.localeCompare(b.name));
  attributes.sort((a, b) => a.name.localeCompare(b.name));
  return { roles, attributes };
}

// A role's characteristics table: what it is (abstract, deprecated), what it derives from, and which states and
// properties it requires, supports and prohibits.
function role(node, kind) {
  return {
    name: text(find(node, n => n.nodeName === "rdef") ?? node),
    abstract: text(cell(node, "role-abstract") ?? node) === "True" || undefined,
    deprecated: kind.includes("deprecated") || undefined,
    superclass: refs(cell(node, "role-parent"), ["rref"]),
    required: refs(cell(node, "role-required-properties"), ["pref", "sref"]),
    supported: refs(cell(node, "role-properties"), ["pref", "sref"]),
    prohibited: refs(cell(node, "role-disallowed"), ["pref", "sref"]),
    nameProhibited: /prohibited/.test(text(cell(node, "role-namefrom") ?? node)) || undefined,
    spec: `https://w3c.github.io/aria/#${attr(node, "id")}`,
  };
}

// A state or property: its value type, its keywords and its default. The value type is the link's TEXT: two
// string-valued properties (aria-colindextext, aria-rowindextext) link `valuetype_integer` by mistake.
function attribute(node, kind) {
  const valueCell = cell(node, "property-value") ?? cell(node, "state-value");
  const value = text(valueCell ?? node).toLowerCase().replace(/^id reference/, "idref").replace(/[ /]/g, "_");
  // One row per keyword, the default marked "(default)". A row of several words is the default spelled as a
  // combination of the others (aria-relevant's "additions text"), not a keyword of its own.
  const values = [];
  let dflt;
  for (const th of [...walk(node)].filter(n => n.nodeName === "th" && classes(n).includes("value-name"))) {
    const word = text(th).replace(/\(default\)/, "").replace(/:$/, "").trim();
    if (/\(default\)/.test(text(th))) dflt = word;
    if (!word.includes(" ")) values.push(word);
  }
  return {
    name: attr(node, "id"),
    kind: kind.includes("state") ? "state" : "property",
    deprecated: kind.includes("deprecated") || undefined,
    value,
    values: values.length ? values : undefined,
    default: dflt,
    spec: `https://w3c.github.io/aria/#${attr(node, "id")}`,
  };
}

const attr = (node, name) => node.attrs?.find(a => a.name === name)?.value;
const classes = node => (attr(node, "class") ?? "").split(/\s+/).filter(Boolean);

function* walk(node) {
  yield node;
  for (const child of node.childNodes ?? node.content?.childNodes ?? []) yield* walk(child);
}

const find = (node, test) => { for (const n of walk(node)) if (n !== node && test(n)) return n; return undefined; };
const text = node => [...walk(node)].filter(n => n.nodeName === "#text").map(n => n.value).join("").replace(/\s+/g, " ").trim();
// A characteristics table's cell, by its class: `role-parent`, `property-value`.
const cell = (def, cls) => find(def, n => n.nodeName === "td" && classes(n).includes(cls));
// The roles (<rref>) or the states and properties (<pref>, <sref>) a cell links, once each, sorted.
const refs = (td, tags) => td ? [...new Set([...walk(td)].filter(n => tags.includes(n.nodeName)).map(text))].sort() : [];
