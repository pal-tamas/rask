// Builds src/Rask.Core/Dom/mdn.snapshot.json from MDN's own data: @webref/elements (which interface each
// tag uses), @webref/idl (the WebIDL MDN's pages are written from) and @mdn/browser-compat-data (what
// exists, what is deprecated, what ships). Run through refresh.sh, which installs the latest of each.
//
// The snapshot is pure MDN data. Mapping it to C# (names, types, aliases) is the generator's job.

import { createRequire } from "node:module";
import { readFileSync, writeFileSync } from "node:fs";
import { resolve } from "node:path";

const require = createRequire(resolve(process.argv[2] ?? ".", "package.json"));
const out = process.argv[3];
if (!out) throw new Error("usage: node refresh.mjs <node_modules dir> <snapshot path>");

const webidl = require("webidl2");
const idlPkg = require("@webref/idl");
const elementsPkg = require("@webref/elements");
const bcd = require("@mdn/browser-compat-data");
const version = name => JSON.parse(readFileSync(resolve(process.argv[2], "node_modules", name, "package.json"), "utf8")).version;

// An element, attribute or member ships when two of the three engines have it unflagged.
const ENGINES = ["chrome", "firefox", "safari"];
// https://html.spec.whatwg.org/multipage/syntax.html#void-elements
const VOID = ["area", "base", "br", "col", "embed", "hr", "img", "input", "link", "meta", "source", "track", "wbr"];
// Specs whose elements are HTML or SVG. MathML is out of scope.
const SPECS = { html: "html", "html-ruby-extensions": "html", SVG2: "svg", "filter-effects-1": "svg", "css-masking-1": "svg", "svg-animations": "svg" };

function ships(compat) {
  if (!compat) return false;
  const s = compat.status ?? {};
  if (s.deprecated || s.standard_track === false) return false;
  let engines = 0;
  for (const browser of ENGINES) {
    const entries = [compat.support?.[browser]].flat().filter(Boolean);
    const ok = entries.some(e =>
      typeof e.version_added === "string" && e.version_added !== "preview" &&
      !e.version_removed && !e.flags && !e.prefix && !e.alternative_name && !e.partial_implementation);
    if (ok) engines++;
  }
  return engines >= 2;
}

const experimental = compat => compat?.status?.experimental === true || undefined;
const mdnUrl = compat => compat?.mdn_url;
const specUrl = compat => [compat?.spec_url].flat()[0];
// The first unflagged, unprefixed version each engine shipped it in: the doc comment's support line and
// the targets analyzer's table. Runtime checks never read it; they detect the feature itself.
function support(compat) {
  const out = {};
  for (const browser of ENGINES) {
    const e = [compat?.support?.[browser]].flat().filter(Boolean).find(x =>
      typeof x.version_added === "string" && !x.version_removed && !x.flags && !x.prefix && !x.alternative_name);
    if (e) out[browser] = e.version_added.replace(/^≤/, "");
  }
  return out;
}
const meta = compat => ({ experimental: experimental(compat), support: support(compat), mdn: mdnUrl(compat), spec: specUrl(compat) });

// ---- IDL: merge partials and mixins -------------------------------------------------------------
const interfaces = new Map(), mixins = new Map(), includes = [], enums = new Map(), dictionaries = new Map();
function merge(map, def) {
  const cur = map.get(def.name);
  if (!cur) map.set(def.name, { name: def.name, inheritance: def.inheritance ?? null, members: [...def.members], extAttrs: def.extAttrs ?? [] });
  else { cur.members.push(...def.members); if (!def.partial && def.inheritance) cur.inheritance = def.inheritance; }
}
for (const ast of Object.values(await idlPkg.parseAll())) {
  for (const def of ast) {
    if (def.type === "interface") merge(interfaces, def);
    else if (def.type === "interface mixin") merge(mixins, def);
    else if (def.type === "includes") includes.push(def);
    else if (def.type === "enum") enums.set(def.name, def.values.map(v => v.value));
    else if (def.type === "dictionary") merge(dictionaries, def);
  }
}
for (const inc of includes) {
  const target = interfaces.get(inc.target), mixin = mixins.get(inc.includes);
  if (target && mixin) target.members.push(...mixin.members.map(m => ({ ...m, mixin: inc.includes })));
}

function typeOf(t) {
  if (!t) return "undefined";
  let s;
  if (t.union) s = "(" + t.idlType.map(typeOf).join(" or ") + ")";
  else if (t.generic) s = `${t.generic}<${t.idlType.map(typeOf).join(", ")}>`;
  else s = typeof t.idlType === "string" ? t.idlType : typeOf(t.idlType);
  return s + (t.nullable ? "?" : "");
}
const ext = (m, name) => m.extAttrs?.find(a => a.name === name);
const extValue = a => a?.rhs ? (Array.isArray(a.rhs.value) ? a.rhs.value.map(v => v.value ?? v) : a.rhs.value) : undefined;

// ---- Elements ------------------------------------------------------------------------------------
const all = await elementsPkg.listAll();
const elements = [];
for (const [spec, ns] of Object.entries(SPECS)) {
  for (const e of all[spec]?.elements ?? []) {
    if (!e.interface || e.obsolete) continue;
    const compat = bcd[ns].elements[e.name]?.__compat;
    if (!ships(compat)) continue;
    if (elements.some(x => x.tag === e.name && x.namespace === ns)) continue;
    elements.push({ tag: e.name, namespace: ns, interface: e.interface, void: VOID.includes(e.name) || undefined, ...meta(compat) });
  }
}
elements.sort((a, b) => a.namespace.localeCompare(b.namespace) || a.tag.localeCompare(b.tag));

// ---- Interfaces: every element interface plus its ancestors ---------------------------------------
const wanted = new Set();
for (const e of elements) for (let n = e.interface; n && !wanted.has(n); n = interfaces.get(n)?.inheritance) wanted.add(n);

function membersOf(name) {
  const def = interfaces.get(name), api = bcd.api[name] ?? {};
  const attributes = [], members = [];
  for (const m of def.members) {
    if (!m.name || m.special === "static") continue;
    if (m.type !== "attribute" && m.type !== "operation") continue;
    const compat = api[m.name]?.__compat;
    if (!ships(compat)) continue;
    const type = m.type === "attribute" ? typeOf(m.idlType) : undefined;
    if (type === "EventHandler" || type === "EventHandler?") continue; // events are Rask's own surface
    const entry = m.type === "attribute"
      ? { kind: "attribute", name: m.name, type, readonly: m.readonly || undefined }
      : { kind: "operation", name: m.name, returns: typeOf(m.idlType),
          args: m.arguments.map(a => ({ name: a.name, type: typeOf(a.idlType), optional: a.optional || undefined, variadic: a.variadic || undefined })) };
    const reflect = ext(m, "Reflect");
    if (reflect) entry.reflect = extValue(reflect) ?? m.name.toLowerCase();
    const def1 = extValue(ext(m, "ReflectDefault"));
    if (def1 !== undefined) entry.reflectDefault = def1;
    const range = extValue(ext(m, "ReflectRange"));
    if (range !== undefined) entry.reflectRange = range.map(Number);
    if (m.mixin) entry.mixin = m.mixin;
    Object.assign(entry, meta(compat));
    // Overloads collapse to the first one that ships; the rest are recorded as args variants.
    const prior = members.find(x => x.kind === "operation" && x.name === m.name);
    if (prior) { (prior.overloads ??= []).push(entry.args); continue; }
    members.push(entry);
  }
  return { members };
}

const interfaceOut = {};
for (const name of [...wanted].sort()) {
  const def = interfaces.get(name);
  if (!def) continue;
  const compat = bcd.api[name]?.__compat;
  const tags = elements.filter(e => e.interface === name);
  const { members } = membersOf(name);
  // Content attributes this interface's tags carry, per BCD, in IDL order: that order is the render order.
  const attributes = [];
  const ns = tags[0]?.namespace;
  for (const tag of tags) {
    const bcdEl = bcd[tag.namespace].elements[tag.tag];
    for (const [attr, data] of Object.entries(bcdEl)) {
      if (attr === "__compat" || !ships(data.__compat)) continue;
      if (attributes.some(a => a.attr === attr)) { attributes.find(a => a.attr === attr).tags.push(tag.tag); continue; }
      const idl = findReflecting(name, attr);
      attributes.push({ attr, property: idl?.name, type: idl?.type, readonly: idl?.readonly, reflect: idl?.reflect !== undefined || undefined,
        on: idl?.on, tags: [tag.tag], ...meta(data.__compat) });
    }
  }
  const order = a => { const i = members.findIndex(m => m.name === a.property); return i < 0 ? 1e6 : i; };
  attributes.sort((a, b) => order(a) - order(b) || a.attr.localeCompare(b.attr));
  for (const a of attributes) if (a.tags.length === tags.length) delete a.tags; // on every tag of the interface
  interfaceOut[name] = { parent: def.inheritance, abstract: tags.length === 0 || undefined, namespace: ns,
    ...meta(compat), attributes: attributes.length ? attributes : undefined, members };
}

// The IDL attribute that reflects a content attribute, searched up the interface chain.
function findReflecting(iface, attr) {
  const flat = attr.replace(/-/g, "").toLowerCase();
  for (let n = iface; n; n = interfaces.get(n)?.inheritance) {
    const def = interfaces.get(n);
    if (!def) break;
    for (const m of def.members) {
      if (m.type !== "attribute" || !m.name) continue;
      const r = ext(m, "Reflect");
      const reflects = r ? (extValue(r) ?? m.name.toLowerCase()) === attr : m.name.toLowerCase() === flat;
      if (reflects) return { name: m.name, type: typeOf(m.idlType), readonly: m.readonly || undefined, reflect: r ? true : undefined, on: n === iface ? undefined : n };
    }
  }
  return undefined;
}

// ---- Global attributes ---------------------------------------------------------------------------
const globals = {};
for (const ns of ["html", "svg"]) {
  globals[ns] = Object.entries(bcd[ns].global_attributes ?? {})
    .filter(([name, d]) => name !== "data_attributes" && ships(d.__compat))
    .map(([name]) => name).sort();
}

// ---- Enums and dictionaries the members reach (for typed refs) -----------------------------------
const reachedEnums = {}, reachedDicts = {};
function reach(type) {
  for (const n of type.match(/[A-Za-z_][A-Za-z0-9_]*/g) ?? []) {
    if (enums.has(n) && !reachedEnums[n]) reachedEnums[n] = enums.get(n);
    const d = dictionaries.get(n);
    if (d && !reachedDicts[n]) {
      reachedDicts[n] = { parent: d.inheritance ?? undefined, members: d.members.filter(m => m.name).map(m => ({ name: m.name, type: typeOf(m.idlType), required: m.required || undefined })) };
      if (d.inheritance) reach(d.inheritance);
      for (const m of reachedDicts[n].members) reach(m.type);
    }
  }
}
for (const i of Object.values(interfaceOut)) for (const m of i.members) { reach(m.type ?? m.returns); for (const a of m.args ?? []) reach(a.type); }
const sortObj = o => Object.fromEntries(Object.entries(o).sort(([a], [b]) => a.localeCompare(b)));

const snapshot = {
  schema: 1,
  sources: Object.fromEntries(["@mdn/browser-compat-data", "@webref/elements", "@webref/idl"].map(p => [p, version(p)])),
  engines: ENGINES,
  elements,
  globalAttributes: globals,
  interfaces: interfaceOut,
  enums: sortObj(reachedEnums),
  dictionaries: sortObj(reachedDicts),
};
writeFileSync(out, JSON.stringify(snapshot, null, 1) + "\n");
console.log(`${elements.length} elements, ${Object.keys(interfaceOut).length} interfaces → ${out}`);
