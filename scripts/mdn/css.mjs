// The CSS half of the snapshot: every property that ships, with what its value may be, read from @webref/css
// (the value grammars of the CSS specs MDN's pages are written from) and @mdn/browser-compat-data (what ships).
//
// A property's grammar is reduced to what a typed call can say in ONE value: the keywords that are a whole value
// on their own (`display: grid`, never the `flow` of `display: block flow`), and the value types that are
// (`width: <length>`). Everything else a grammar allows is still CSS, and is written as text. Only names and
// grammars are read — data, never a spec's prose.

// Valid for every property, so said once by the emitter rather than in each property's set.
const CSS_WIDE = new Set(["inherit", "initial", "unset", "revert", "revert-layer"]);
// Value types kept whole. A colour's grammar reaches 150 names, and `color: red` is not a keyword of `color`; the
// rest are defined in terms of the tokenizer, which says nothing a caller can use.
const OPAQUE = new Set(["color", "integer", "number", "string", "custom-ident", "dashed-ident"]);

export function readCss(css, bcd, { ships, meta }) {
  const types = new Map();
  // A type defined once per scope (`<content-list>`) is read where it is not scoped, or as first given.
  for (const t of css.types) if (!types.has(t.name) || !t.for) types.set(t.name, t);
  const byName = new Map(css.properties.map(p => [p.name, p]));
  const reduce = reducer(types, byName);

  const properties = [];
  for (const p of css.properties) {
    const tree = bcd.css.properties[p.name];
    // A vendor-prefixed name and a legacy alias are another property's old spelling.
    if (p.name.startsWith("-") || p.legacyAliasOf || !p.syntax || !ships(tree?.__compat)) continue;
    const value = reduce(p.syntax);
    // A keyword BCD tracks and says has not shipped (`display: grid-lanes`) is not offered; one it does not track is.
    const keywords = [...value.keywords].filter(k => !CSS_WIDE.has(k) && (!tree[k]?.__compat || ships(tree[k].__compat)));
    properties.push({
      name: p.name, keywords, accepts: [...value.types].sort(),
      longhands: p.longhands, ...meta(tree.__compat), spec: p.href,
    });
  }

  if (properties.length < 300) throw new Error(`@webref/css: read ${properties.length} properties that ship; has the data's shape changed?`);
  properties.sort((a, b) => a.name.localeCompare(b.name));
  return { properties };
}

// CSS value definition syntax (https://drafts.csswg.org/css-values-4/#value-defs), reduced as it is parsed.
// A result is { keywords, types, nullable }: what is a whole value alone, and whether nothing is.
function reducer(types, properties) {
  const memo = new Map();
  const nothing = () => ({ keywords: new Set(), types: new Set(), nullable: false });

  function named(key, syntax) {
    if (memo.has(key)) return memo.get(key) ?? nothing(); // null while it is being read: a grammar that names itself
    memo.set(key, null);
    const result = reduce(syntax);
    memo.set(key, result);
    return result;
  }

  function reference(token) {
    const quoted = /^<'([^']+)'/.exec(token);
    if (quoted) return properties.has(quoted[1]) ? named("'" + quoted[1], properties.get(quoted[1]).syntax ?? "") : nothing();
    const name = /^<([^\s>\[]+)/.exec(token)[1];
    if (name.endsWith("()")) return nothing();
    const syntax = OPAQUE.has(name) ? undefined : types.get(name)?.syntax;
    return syntax ? named(name, syntax) : { ...nothing(), types: new Set([name]) };
  }

  function reduce(syntax) {
    const tokens = syntax.match(/<[^>]*>|\|\||&&|\{[^}]*\}|[A-Za-z_-][\w-]*\(?|\d[\w.]*|[^\s]/g) ?? [];
    let at = 0;
    const stops = new Set(["|", "||", "&&", "]", ")"]);

    const union = parts => parts.length === 1 ? parts[0] : {
      keywords: new Set(parts.flatMap(p => [...p.keywords])), types: new Set(parts.flatMap(p => [...p.types])),
      nullable: parts.some(p => p.nullable),
    };
    // All of them, in order or not: one is a whole value alone only where every other may be left out.
    const all = parts => parts.length === 1 ? parts[0] : {
      keywords: new Set(parts.flatMap((p, i) => parts.every((o, j) => i === j || o.nullable) ? [...p.keywords] : [])),
      types: new Set(parts.flatMap((p, i) => parts.every((o, j) => i === j || o.nullable) ? [...p.types] : [])),
      nullable: parts.every(p => p.nullable),
    };
    const list = (next, separator, join) => {
      const parts = [next()];
      while (tokens[at] === separator) { at++; parts.push(next()); }
      return join(parts);
    };

    const choice = () => list(anyOf, "|", union);
    // `a || b`: one or more of them, so each is a value alone.
    const anyOf = () => list(allOf, "||", parts => ({ ...union(parts), nullable: false }));
    const allOf = () => list(sequence, "&&", all);
    function sequence() {
      const parts = [];
      while (at < tokens.length && !stops.has(tokens[at])) parts.push(term());
      return parts.length ? all(parts) : nothing();
    }

    function term() {
      let value = atom();
      for (; at < tokens.length && /^([?*+#!]|\{.*\})$/.test(tokens[at]); at++) {
        const m = tokens[at];
        const optional = m === "?" || m === "*" || /^\{0/.test(m);
        value = { ...value, nullable: m === "!" ? false : value.nullable || optional };
      }
      return value;
    }

    function atom() {
      const token = tokens[at++];
      if (token === "[") { const inner = choice(); at++; return inner; }
      if (token.startsWith("<")) return reference(token);
      if (token.endsWith("(")) { choice(); at++; return nothing(); }
      if (/^[A-Za-z_-]/.test(token)) return { ...nothing(), keywords: new Set([token]) };
      return nothing(); // a literal: `,` or `/`
    }

    return choice();
  }

  return reduce;
}
