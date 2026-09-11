// Rask package-island props extractor.
//
// Reads the TypeScript declarations an npm component ships and writes the props snapshot a package island's
// C# is generated from (`{Island}.props.json`, schema 1 — see docs/islands.md#using-a-package-component-directly).
//
// Run by the build, never by hand:  node rask-extract-props.mjs <request.json>
//
// The TypeScript compiler it loads is the one RASK pins and fetches (request.typescript), never the project's
// own copy: two machines with different `typescript` versions installed must extract the same snapshot, or a
// committed file flips back and forth between them.
//
// Every island fails on its own. One package that is missing, or an export that is not a component, is recorded
// in result.json with a code the build turns into RASKISLAND007 at the island's Module line; the other islands'
// snapshots are still written.

import {createRequire} from "node:module";
import * as fs from "node:fs";
import * as path from "node:path";
import type * as TS from "typescript";

// ----- request / result ---------------------------------------------------------------------------------------

interface IslandRequest {
    name: string;
    runtime: string;
    module: string;
    export: string;
    out: string;
}

interface ExtractRequest {
    typescript: string;
    projectDirectory: string;
    islands: IslandRequest[];
}

interface IslandResult {
    name: string;
    ok: boolean;
    code?: string;
    message?: string;
}

// ----- snapshot model (schema 1) ------------------------------------------------------------------------------

type SnapshotType = {
    kind: string;
    nullable?: boolean;
    base?: "string" | "number";
    values?: (string | number | boolean)[];
    open?: boolean;
    element?: SnapshotType;
    value?: SnapshotType;
    of?: SnapshotType[];
    members?: SnapshotMember[];
    name?: string;
    args?: {name: string; optional?: boolean; type: SnapshotType}[];
    returns?: boolean;
};

interface SnapshotMember {
    name: string;
    required: boolean;
    doc?: string;
    type: SnapshotType;
}

interface SnapshotProp {
    name: string;
    /** The key the prop travels under, when it is not `name`: an Angular input's alias, an `@event` to subscribe to. */
    wire?: string;
    required: boolean;
    doc?: string;
    default?: string;
    type: SnapshotType;
}

interface Skip {
    name: string;
    reason: string;
    detail?: string;
}

/** Why a prop is left out: carried up the type walk so the prop lands in `skipped` with its reason. */
class Skipped extends Error {
    constructor(readonly reason: string, readonly detail?: string) {
        super(reason);
    }
}

// ----- per-runtime knowledge ----------------------------------------------------------------------------------

/**
 * The packages that ARE the framework. A prop whose every declaration lives in one of these — a DOM attribute
 * from @types/react, a style from csstype — is inherited plumbing, not the component's own surface, and
 * listing hundreds of them would bury the props that matter and churn with every @types release.
 */
const FrameworkPackages: Record<string, string[]> = {
    react: ["@types/react", "@types/react-dom", "csstype", "@types/prop-types"],
    preact: ["preact", "csstype"],
    solid: ["solid-js", "csstype"],
    // @vue/reactivity so a `Ref` counts as the framework's; runtime-dom owns the DOM attributes a component spreads.
    vue: ["vue", "@vue/runtime-core", "@vue/runtime-dom", "@vue/reactivity", "@vue/shared", "csstype"],
    // svelte/elements.d.ts belongs to the svelte package, so its attributes are inherited — unless a component
    // redeclares one in its own file, which is how ownership is decided: by declaration, not by where a type came from.
    svelte: ["svelte", "csstype"],
    // A Lit element's base classes: ReactiveElement's and LitElement's members are the framework's, not the element's.
    lit: ["lit", "lit-element", "lit-html", "@lit/reactive-element"],
    angular: ["@angular/core", "@angular/common", "rxjs"],
};

/** Type names that are rendered content, not data — a child, a slot, an element. */
const NodeTypeNames = new Set([
    "ReactNode", "ReactElement", "ReactPortal", "Element", "JSXElement", "ComponentChildren", "ComponentChild",
    "VNode", "Children", "Snippet",
]);

/** Type names that are refs, which have no meaning across the wire. */
const RefTypeNames = new Set(["Ref", "RefObject", "RefCallback", "LegacyRef", "ForwardedRef", "MutableRefObject"]);

const MaxDepth = 4;

/** Past this many literals a union is a vocabulary to autocomplete from, not a choice to generate an enum for. */
const MaxEnumValues = 64;

// ----- entry --------------------------------------------------------------------------------------------------

function main(): void {
    const requestPath = process.argv[2];
    if (!requestPath) {
        process.stderr.write("usage: rask-extract-props <request.json>\n");
        process.exit(2);
    }

    const request = JSON.parse(fs.readFileSync(requestPath, "utf8")) as ExtractRequest;
    const require = createRequire(import.meta.url);
    const ts = require(request.typescript) as typeof TS;

    const results: IslandResult[] = [];
    const byRuntime = new Map<string, IslandRequest[]>();
    for (const island of request.islands) {
        if (!FrameworkPackages[island.runtime]) {
            results.push({name: island.name, ok: false, code: "runtime-unsupported",
                message: `Rask cannot extract props for the '${island.runtime}' runtime yet.`});
            continue;
        }

        const list = byRuntime.get(island.runtime) ?? [];
        list.push(island);
        byRuntime.set(island.runtime, list);
    }

    for (const [runtime, islands] of [...byRuntime].sort(([a], [b]) => a.localeCompare(b))) {
        results.push(...extractRuntime(ts, request.projectDirectory, runtime, islands));
    }

    results.sort((a, b) => a.name.localeCompare(b.name));
    fs.writeFileSync(path.join(path.dirname(requestPath), "result.json"), JSON.stringify(results, null, 2) + "\n");
}

// ----- one program per runtime --------------------------------------------------------------------------------

function extractRuntime(ts: typeof TS, projectDirectory: string, runtime: string, islands: IslandRequest[]): IslandResult[] {
    // One program per runtime, so React's global JSX namespace cannot collide with Solid's, and a virtual probe
    // that imports every island's export. The probe sits IN the project directory, so module resolution walks up
    // to the project's own node_modules exactly as the bundler will — hoisted monorepos and pnpm included.
    const probePath = path.join(projectDirectory, "__rask_props_probe__.ts");
    const probeText = islands.map((island, i) => probeLines(island, i)).join("")
        // A Lit element's tag is known only to the global tag map, so a Lit program reads the whole map back.
        + (runtime === "lit" ? "export type __rask_tag_map = HTMLElementTagNameMap;\n" : "");

    const options: TS.CompilerOptions = {
        strict: true,
        noEmit: true,
        skipLibCheck: true,
        target: ts.ScriptTarget.ES2022,
        module: ts.ModuleKind.ESNext,
        moduleResolution: ts.ModuleResolutionKind.Bundler,
        esModuleInterop: true,
        allowSyntheticDefaultImports: true,
        jsx: ts.JsxEmit.Preserve,
        // No ambient @types: every one would load globally and pour its declarations into the checker.
        types: [],
        lib: ["lib.es2022.d.ts", "lib.dom.d.ts", "lib.dom.iterable.d.ts"],
        customConditions: runtime === "solid" ? ["solid"] : runtime === "svelte" ? ["svelte"] : undefined,
    };

    const host = ts.createCompilerHost(options, true);
    const getSourceFile = host.getSourceFile.bind(host);
    host.getSourceFile = (fileName, languageVersion, onError, shouldCreate) =>
        path.resolve(fileName) === probePath
            ? ts.createSourceFile(fileName, probeText, languageVersion, true)
            : getSourceFile(fileName, languageVersion, onError, shouldCreate);
    const fileExists = host.fileExists.bind(host);
    host.fileExists = (fileName) => path.resolve(fileName) === probePath || fileExists(fileName);
    const readFile = host.readFile.bind(host);
    host.readFile = (fileName) => (path.resolve(fileName) === probePath ? probeText : readFile(fileName));

    const program = ts.createProgram({rootNames: [probePath], options, host});
    const checker = program.getTypeChecker();
    const probe = program.getSourceFile(probePath)!;

    const tagMap = probe.statements.find((s): s is TS.TypeAliasDeclaration =>
        ts.isTypeAliasDeclaration(s) && s.name.text === "__rask_tag_map");
    return islands.map((island, i) => {
        try {
            const snapshot = runtime === "lit"
                ? extractLit(ts, program, checker, island, probeNode(ts, probe, i), tagMap, probe)
                : runtime === "angular"
                    ? extractAngular(ts, program, checker, island, importOf(ts, probe, i))
                    : extractIsland(ts, program, checker, runtime, island, importOf(ts, probe, i));
            fs.mkdirSync(path.dirname(island.out), {recursive: true});
            fs.writeFileSync(island.out, snapshot);
            return {name: island.name, ok: true};
        } catch (error) {
            if (error instanceof IslandError) {
                return {name: island.name, ok: false, code: error.code, message: error.message};
            }

            return {name: island.name, ok: false, code: "extractor-crashed", message: String(error)};
        }
    });
}

class IslandError extends Error {
    constructor(readonly code: string, message: string) {
        super(message);
    }
}

function extractIsland(
    ts: typeof TS,
    program: TS.Program,
    checker: TS.TypeChecker,
    runtime: string,
    island: IslandRequest,
    declaration: TS.ImportDeclaration | undefined,
): string {
    const binding = importedName(ts, declaration);
    if (!binding) {
        throw new IslandError("module-not-found", `'${island.module}' could not be imported.`);
    }

    const resolved = checker.getSymbolAtLocation(binding);
    const target = resolved && resolved.flags & ts.SymbolFlags.Alias ? checker.getAliasedSymbol(resolved) : resolved;
    if (!target || target.flags & ts.SymbolFlags.Alias || !target.declarations?.length) {
        const found = moduleResolves(ts, program, island.module);
        throw found
            ? new IslandError("export-not-found",
                `'${island.module}' has no ${island.export === "default" ? "default export" : `export named '${island.export}'`}.`)
            : new IslandError("module-not-found",
                `'${island.module}' could not be resolved from the project — install it (npm install ${packageOf(island.module)}).`);
    }

    // A dotted export (`Switch.Root`) names a member of the export: bits-ui and its kind export namespaces of parts.
    let componentType = checker.getTypeOfSymbolAtLocation(target, binding);
    for (const segment of island.export.split(".").slice(1)) {
        const member = componentType.getProperty(segment);
        if (!member) {
            throw new IslandError("export-not-found", `'${island.module}#${island.export}' has no member '${segment}'.`);
        }

        componentType = checker.getTypeOfSymbol(member);
    }

    const propsType = propsOf(ts, checker, componentType, runtime);
    if (!propsType) {
        throw new IslandError("not-a-component",
            `'${island.module}${island.export === "default" ? "" : "#" + island.export}' is not a ${runtime} component: it has no call signature taking props.`);
    }

    const walker = new TypeWalker(ts, checker, runtime);
    const props: SnapshotProp[] = [];
    const skipped: Skip[] = [];

    for (const {name, symbols, required, type: propertyType} of propertiesOf(ts, checker, propsType)) {
        const property = symbols[0];
        if (name === "key" || name === "ref") {
            continue;
        }

        // svelte-package's Svelte 4 typings carry the component's events and slots in its props as `$$events` and
        // `$$slots`: plumbing, read below for events and content, never props of their own.
        if (runtime === "svelte" && name.startsWith("$$")) {
            continue;
        }

        if (name.startsWith("$")) {
            skipped.push({name, reason: "unsupported", detail: "a name starting with '$' is reserved by the props wire"});
            continue;
        }

        // Inherited only when EVERY declaration of it — across a union's members too — is the framework's.
        if (symbols.every((symbol) => walker.isInherited(symbol))) {
            continue;
        }

        try {
            const type = walker.serialize(propertyType, 0, true);
            const doc = docOf(ts, checker, property);
            const fallback = defaultOf(property);

            // Built in the snapshot's fixed key order — the short facts first, the nested type last — because
            // JSON.stringify writes keys in insertion order and a reviewer reads what a prop is before its shape.
            props.push({
                name,
                required,
                ...(doc ? {doc} : {}),
                ...(fallback ? {default: fallback} : {}),
                type,
            });
        } catch (error) {
            if (error instanceof Skipped) {
                // A snippet passed as `children` is the component's content; any other snippet prop is a named part.
                const reason = error.reason === "snippet" && name === "children" ? "node" : error.reason;
                skipped.push(error.detail ? {name, reason, detail: error.detail} : {name, reason});
                continue;
            }

            throw error;
        }
    }

    if (runtime === "vue") {
        synthesizeEmits(ts, checker, componentType, walker, props, skipped);
    }

    if (runtime === "svelte") {
        legacyEvents(ts, checker, componentType, propsType, skipped);
    }

    const content = contentOf(ts, checker, runtime, componentType, propsType, skipped);
    return snapshotText(runtime, island, packageInfo(island.module, target), null, content, props, skipped, walker);
}

/** The snapshot document in its fixed key order, props and skips sorted so it never depends on the order of the walk. */
function snapshotText(
    runtime: string,
    island: IslandRequest,
    pkg: {name: string; version: string | null},
    tag: string | null,
    content: string,
    props: SnapshotProp[],
    skipped: Skip[],
    walker: TypeWalker,
): string {
    props.sort((a, b) => compare(a.name, b.name));
    skipped.sort((a, b) => compare(a.name, b.name));

    const snapshot: Record<string, unknown> = {
        schema: 1,
        runtime,
        module: island.module,
        export: island.export,
        package: pkg,
        tag,
        content,
        props,
        skipped,
    };

    const types = walker.namedTypes();
    if (Object.keys(types).length > 0) {
        snapshot.types = types;
    }

    return JSON.stringify(snapshot, null, 2) + "\n";
}

// ----- component → props --------------------------------------------------------------------------------------

/**
 * The props type of a component value, where each runtime's declarations put it.
 *
 * - React, Preact, Solid: the first parameter of the last non-generic call signature (plain functions, memo,
 *   forwardRef, MUI's OverridableComponent, whose default-props overload comes last), or a class's instance `props`.
 * - Vue: the instance `$props` of a construct signature — what `defineComponent` and vue-tsc's `<script setup>` output
 *   declare, emits already folded in as on* props — else the first parameter of a call signature, which is how a
 *   generic `<script setup>` component and a FunctionalComponent are declared.
 * - Svelte 5: `Component<Props>`'s call signature takes the internals FIRST and the props second. Svelte 4 typings
 *   are a class whose instance carries `$$prop_def`.
 */
function propsOf(ts: typeof TS, checker: TS.TypeChecker, component: TS.Type, runtime: string): TS.Type | undefined {
    const calls = checker.getSignaturesOfType(component, ts.SignatureKind.Call);
    const constructs = checker.getSignaturesOfType(component, ts.SignatureKind.Construct);
    const instanceProperty = (name: string) =>
        constructs.map((c) => checker.getReturnTypeOfSignature(c).getProperty(name)).find((p) => p !== undefined);

    if (runtime === "svelte") {
        const call = calls.find((s) => s.parameters.length === 2);
        if (call) {
            return checker.getTypeOfSymbol(call.parameters[1]);
        }

        const legacy = instanceProperty("$$prop_def");
        return legacy ? checker.getTypeOfSymbol(legacy) : undefined;
    }

    if (runtime === "vue") {
        const $props = instanceProperty("$props");
        if ($props) {
            return checker.getTypeOfSymbol($props);
        }

        const call = calls[calls.length - 1];
        return call && call.parameters.length > 0 ? checker.getTypeOfSymbol(call.parameters[0]) : undefined;
    }

    const call = [...calls].reverse().find((s) => !s.typeParameters?.length) ?? calls[calls.length - 1];
    if (call && call.parameters.length > 0) {
        return checker.getTypeOfSymbol(call.parameters[0]);
    }

    const construct = constructs[constructs.length - 1];
    if (construct) {
        const instance = checker.getReturnTypeOfSignature(construct);
        const props = instance.getProperty("props");
        if (props) {
            return checker.getTypeOfSymbol(props);
        }
    }

    return undefined;
}

/**
 * Every property a props type has — across a union too, where the checker alone would keep only the shared ones —
 * with whether it is required. Across a union a prop is required only when EVERY member requires it: in
 * `{value; onChange} | {defaultValue}` neither half's props are, or the chain would demand two that exclude each other.
 */
function propertiesOf(
    ts: typeof TS,
    checker: TS.TypeChecker,
    props: TS.Type,
): {name: string; symbols: TS.Symbol[]; required: boolean; type: TS.Type}[] {
    const seen = new Map<string, {symbols: TS.Symbol[]; required: boolean; types: TS.Type[]}>();
    const constituents = props.isUnion() ? props.types : [props];
    for (const type of constituents) {
        for (const property of checker.getPropertiesOfType(checker.getApparentType(type))) {
            const propertyType = checker.getTypeOfSymbol(property);
            const required = (property.flags & ts.SymbolFlags.Optional) === 0 && !includesUndefined(ts, propertyType);
            const entry = seen.get(property.getName());
            if (entry) {
                entry.required = entry.required && required;
                entry.symbols.push(property);
                entry.types.push(propertyType);
            } else {
                seen.set(property.getName(), {symbols: [property], required, types: [propertyType]});
            }
        }
    }

    return [...seen].map(([name, entry]) => ({
        name,
        symbols: entry.symbols,
        required: entry.required && entry.symbols.length === constituents.length,
        // Every member's type for it, not the first member's: one half of a union may declare it `never`.
        type: entry.types.length === 1 ? entry.types[0] : checker.getUnionType(entry.types),
    }));
}

// ----- type walk ----------------------------------------------------------------------------------------------

class TypeWalker {
    private readonly named = new Map<string, SnapshotType | null>();
    private readonly names = new Map<TS.Symbol, string>();
    private readonly framework: string[];

    constructor(private readonly ts: typeof TS, private readonly checker: TS.TypeChecker, runtime: string) {
        this.framework = FrameworkPackages[runtime] ?? [];
    }

    /** Whether every declaration of a property lives in the framework or the default library. */
    isInherited(property: TS.Symbol): boolean {
        const declarations = property.declarations ?? [];
        if (declarations.length === 0) {
            return false;
        }

        return declarations.every((d) => this.isFrameworkFile(d.getSourceFile().fileName));
    }

    namedTypes(): Record<string, SnapshotType> {
        const out: Record<string, SnapshotType> = {};
        for (const name of [...this.named.keys()].sort(compare)) {
            const type = this.named.get(name);
            if (type) {
                out[name] = type;
            }
        }

        return out;
    }

    /**
     * Serializes a type into the snapshot's kinds.
     * @param top Whether this is a prop's own type, where node and ref types are skipped rather than refused.
     */
    serialize(type: TS.Type, depth: number, top: boolean): SnapshotType {
        const {ts, checker} = this;
        if (depth > MaxDepth) {
            throw new Skipped("too-deep");
        }

        if (type.flags & ts.TypeFlags.Any) {
            throw new Skipped("any");
        }

        // A type parameter nothing instantiated — vue-tsc's generic `<script setup>` component, a generic Svelte
        // part — stands for its default, or its constraint; with neither, the prop is honestly generic.
        if (type.flags & ts.TypeFlags.TypeParameter) {
            const substitute = checker.getDefaultFromTypeParameter(type) ?? checker.getBaseConstraintOfType(type);
            if (!substitute || substitute === type || substitute.flags & ts.TypeFlags.Unknown) {
                throw new Skipped("generic", checker.typeToString(type));
            }

            return this.serialize(substitute, depth + 1, top);
        }

        const alias = type.aliasSymbol?.getName() ?? type.getSymbol()?.getName();
        if (alias && NodeTypeNames.has(alias) && this.isFrameworkOwned(type)) {
            throw new Skipped(alias === "Snippet" ? "snippet" : "node");
        }

        if (alias && RefTypeNames.has(alias) && this.isFrameworkOwned(type)) {
            throw new Skipped("ref");
        }

        if (type.isUnion()) {
            return this.serializeUnion(type, depth, top);
        }

        if (type.flags & ts.TypeFlags.Intersection) {
            // `string & {}` is the idiom for "any string, but keep the literals in autocomplete".
            const parts = (type as TS.IntersectionType).types;
            if (parts.some((t) => t.flags & ts.TypeFlags.String)) {
                return {kind: "string"};
            }
        }

        if (type.flags & (ts.TypeFlags.String | ts.TypeFlags.TemplateLiteral)) {
            return {kind: "string"};
        }

        if (type.flags & ts.TypeFlags.Number) {
            return {kind: "number"};
        }

        if (type.flags & ts.TypeFlags.BigInt) {
            throw new Skipped("unsupported", "bigint");
        }

        if (type.flags & ts.TypeFlags.Boolean || type.flags & ts.TypeFlags.BooleanLiteral) {
            return {kind: "boolean"};
        }

        if (type.isStringLiteral()) {
            return {kind: "enum", base: "string", values: [type.value]};
        }

        if (type.isNumberLiteral()) {
            return {kind: "enum", base: "number", values: [type.value]};
        }

        if (type.flags & ts.TypeFlags.Unknown || type.flags & ts.TypeFlags.Never) {
            throw new Skipped("unsupported", checker.typeToString(type));
        }

        const symbol = type.getSymbol();
        if (symbol?.getName() === "Date" && this.isLibFile(symbol)) {
            return {kind: "date"};
        }

        if (checker.isArrayType(type) || checker.isTupleType(type)) {
            const args = checker.getTypeArguments(type as TS.TypeReference);
            const element = args.length === 1 ? args[0] : checker.getUnionType([...args]);
            return {kind: "array", element: this.serialize(element, depth + 1, false)};
        }

        const calls = checker.getSignaturesOfType(type, ts.SignatureKind.Call);
        if (calls.length > 0) {
            if (!top) {
                throw new Skipped("unsupported", "a function inside another value");
            }

            return this.serializeCallback(calls[calls.length - 1], depth);
        }

        const index = checker.getIndexInfoOfType(type, ts.IndexKind.String);
        if (index && checker.getPropertiesOfType(type).length === 0) {
            return {kind: "record", value: this.serialize(index.type, depth + 1, false)};
        }

        if (type.flags & ts.TypeFlags.Object) {
            return this.serializeObject(type, depth);
        }

        throw new Skipped("unsupported", checker.typeToString(type));
    }

    private serializeUnion(union: TS.UnionType, depth: number, top: boolean): SnapshotType {
        const {ts} = this;
        let nullable = false;
        const rest: TS.Type[] = [];
        for (const member of union.types) {
            if (member.flags & ts.TypeFlags.Null) {
                nullable = true;
            } else if (member.flags & (ts.TypeFlags.Undefined | ts.TypeFlags.Void)) {
                // Optionality, which the prop's `required` already says.
            } else {
                rest.push(member);
            }
        }

        const withNull = (t: SnapshotType): SnapshotType => (nullable ? {...t, nullable: true} : t);

        if (rest.length === 0) {
            throw new Skipped("unsupported", "null or undefined only");
        }

        if (rest.length === 1) {
            return withNull(this.serialize(rest[0], depth, top));
        }

        // `true | false` is how the checker spells boolean.
        if (rest.every((t) => t.flags & ts.TypeFlags.BooleanLiteral)) {
            return withNull({kind: "boolean"});
        }

        // `string | boolean` spells its boolean as `true | false` too. Beside an OPEN string those two literals would
        // become an enum of "false" and "true"; it is a boolean beside a string. Beside other literals — MUI's
        // `'auto' | true | false` — they stay values of one enum, which is exactly what the package accepts.
        const booleans = rest.filter((t) => t.flags & ts.TypeFlags.BooleanLiteral);
        const remaining = rest.filter((t) => !(t.flags & ts.TypeFlags.BooleanLiteral));
        if (booleans.length === 2 && remaining.every((t) => this.isStringy(t) && !t.isStringLiteral())) {
            const inner = remaining.length === 1
                ? this.serialize(remaining[0], depth, top)
                : this.serialize(this.checker.getUnionType(remaining), depth, top);
            return withNull(this.unionOf([{kind: "boolean"}, inner]));
        }

        const literals = rest.filter((t) =>
            t.isStringLiteral() || t.isNumberLiteral() || (t.flags & ts.TypeFlags.BooleanLiteral) !== 0);
        const others = rest.filter((t) => !literals.includes(t));

        if (literals.length > 0) {
            // Literals plus an open string — `string`, `string & {}`, or a pattern such as `section-${string}` — are
            // still an enum, but one the package extends.
            const opensWithString = others.length > 0 && others.every((t) => this.isStringy(t));
            if (others.length === 0 || opensWithString) {
                return withNull(this.enumOf(literals, opensWithString));
            }

            // Literals beside other kinds (`"auto" | number`): the literals are ONE enum, beside the rest.
            return withNull(this.unionOf([
                this.enumOf(literals, false),
                ...others.map((t) => this.serialize(t, depth, top)),
            ]));
        }

        return withNull(this.unionOf(rest.map((t) => this.serialize(t, depth, top))));
    }

    /**
     * Literal types as one enum. Past MaxEnumValues the union is a vocabulary rather than a choice — a generated C#
     * enum of hundreds of members helps nobody — and it crosses as the string or number it is.
     */
    private enumOf(literals: TS.Type[], open: boolean): SnapshotType {
        const values = [...new Set(literals.map((t) =>
            t.isStringLiteral() ? t.value : t.isNumberLiteral() ? t.value : this.checker.typeToString(t) === "true"))];
        if (values.every((v) => typeof v === "boolean")) {
            return {kind: "boolean"};
        }

        const base = values.every((v) => typeof v === "number") ? "number" : "string";
        if (values.length > MaxEnumValues) {
            return {kind: base};
        }

        values.sort((a, b) => (typeof a === "number" && typeof b === "number" ? a - b : compare(String(a), String(b))));
        return open ? {kind: "enum", base, values, open: true} : {kind: "enum", base, values};
    }

    /** Kinds as one kind, or one union of the distinct ones, in an order that depends on content alone. */
    private unionOf(kinds: SnapshotType[]): SnapshotType {
        const flat = kinds.flatMap((k) => (k.kind === "union" && !k.nullable ? k.of ?? [] : [k]));
        const distinct = [...new Map(flat.map((k) => [JSON.stringify(k), k])).values()];
        if (distinct.length === 1) {
            return distinct[0];
        }

        // Ties broken by content: members of one kind otherwise stay in the checker's type-id order, which moves
        // with whatever else the shared program checked first — and a committed snapshot must not.
        distinct.sort((a, b) => compare(a.kind, b.kind) || compare(JSON.stringify(a), JSON.stringify(b)));
        return {kind: "union", of: distinct};
    }

    /**
     * A callback's arguments and whether it returns anything.
     * @param skip Leading parameters that are not arguments — the event name of a Vue `$emit` overload.
     */
    serializeCallback(signature: TS.Signature, depth: number, skip = 0): SnapshotType {
        const {ts, checker} = this;
        const args = signature.parameters.slice(skip).flatMap((parameter, index) => {
            const declaration = parameter.valueDeclaration as TS.ParameterDeclaration | undefined;
            const type = checker.getTypeOfSymbol(parameter);

            // `(...args: [value: number, source: string]) => any` is how Vue types an emit handler: the tuple's
            // elements are the arguments, named by their labels.
            if (declaration?.dotDotDotToken && checker.isTupleType(type)) {
                const tuple = (type as TS.TypeReference).target as TS.TupleType;
                return checker.getTypeArguments(type as TS.TypeReference).map((element, k) => {
                    const label = tuple.labeledElementDeclarations?.[k]?.name;
                    return {
                        name: argumentName(label && ts.isIdentifier(label) ? label.text : undefined, index + k),
                        ...(tuple.elementFlags[k] & ts.ElementFlags.Optional ? {optional: true} : {}),
                        type: this.argument(element, depth),
                    };
                });
            }

            const optional = !!declaration && (!!declaration.questionToken || !!declaration.initializer);
            return [{
                name: argumentName(parameter.getName(), index),
                ...(optional ? {optional: true} : {}),
                type: this.argument(type, depth),
            }];
        });

        const result: SnapshotType = {kind: "callback", args};
        const returned = checker.getReturnTypeOfSignature(signature);
        if (!(returned.flags & (ts.TypeFlags.Void | ts.TypeFlags.Undefined | ts.TypeFlags.Any | ts.TypeFlags.Unknown))) {
            result.returns = true;
        }

        return result;
    }

    /**
     * What an Angular output emits: described when it is a value the C# side can take, `unknown` when it is an object —
     * a change record whose `source` is the live component cannot cross, and walking it would describe the component.
     */
    payload(type: TS.Type): SnapshotType {
        const {ts, checker} = this;
        const symbol = type.getSymbol();
        const date = symbol?.getName() === "Date" && this.isLibFile(symbol);
        if (type.flags & ts.TypeFlags.Object && !date && !checker.isArrayType(type)) {
            return {kind: "unknown"};
        }

        return this.argument(type, 0);
    }

    /** A callback argument: an event stays an event (it never crosses), anything else is described if it can be. */
    private argument(type: TS.Type, depth: number): SnapshotType {
        const name = type.aliasSymbol?.getName() ?? type.getSymbol()?.getName();
        if (name && /Event$/.test(name)) {
            return {kind: "event", name};
        }

        try {
            return this.serialize(type, depth + 1, false);
        } catch (error) {
            if (error instanceof Skipped) {
                return {kind: "unknown"};
            }

            throw error;
        }
    }

    private serializeObject(type: TS.Type, depth: number): SnapshotType {
        const {ts, checker} = this;
        const symbol = type.aliasSymbol ?? type.getSymbol();
        const declared = symbol?.declarations?.[0];
        const anonymous = !symbol || symbol.getName() === "__type" || symbol.getName() === "__object";

        // A DOM or framework type — HTMLElement, CSSProperties, Window — is a live object, not data, and walking it
        // inlines hundreds of members per level. It cannot cross the wire, so it is skipped by name.
        if (declared && this.isFrameworkFile(declared.getSourceFile().fileName)) {
            throw new Skipped("unsupported", checker.typeToString(type));
        }

        // A generic instantiation shares its name with every other one (Option<string>, Option<number>), so it is
        // described inline rather than registered under a name it does not own.
        const reference = (type.flags & ts.TypeFlags.Object) !== 0
            && ((type as TS.ObjectType).objectFlags & ts.ObjectFlags.Reference) !== 0;
        const generic = (type.aliasTypeArguments?.length ?? 0) > 0
            || (reference && checker.getTypeArguments(type as TS.TypeReference).length > 0);

        if (!anonymous && !generic && symbol) {
            return {kind: "ref", name: this.nameFor(symbol, type, depth)};
        }

        return this.members(type, depth);
    }

    /** The name a named type is registered under: its own, suffixed when a different type already took it. */
    private nameFor(symbol: TS.Symbol, type: TS.Type, depth: number): string {
        const existing = this.names.get(symbol);
        if (existing) {
            return existing;
        }

        const base = symbol.getName();
        let name = base;
        for (let n = 2; this.named.has(name); n++) {
            name = `${base}${n}`;
        }

        // Registered before the walk, so a type that reaches itself refers back rather than recursing.
        this.names.set(symbol, name);
        this.named.set(name, null);
        this.named.set(name, this.members(type, depth));
        return name;
    }

    private members(type: TS.Type, depth: number): SnapshotType {
        const {ts, checker} = this;
        const members: SnapshotMember[] = [];
        for (const property of checker.getPropertiesOfType(type)) {
            const propertyType = checker.getTypeOfSymbol(property);
            try {
                // Serialized first: a member that cannot cross throws Skipped before anything is pushed.
                const type = this.serialize(propertyType, depth + 1, false);
                const doc = docOf(ts, checker, property);
                members.push({
                    name: property.getName(),
                    required: (property.flags & ts.SymbolFlags.Optional) === 0 && !includesUndefined(ts, propertyType),
                    ...(doc ? {doc} : {}),
                    type,
                });
            } catch (error) {
                if (!(error instanceof Skipped)) {
                    throw error;
                }
                // A member that cannot cross is left out of the object; the resolver decides whether that makes
                // the whole prop unusable (a required member) or not.
            }
        }

        members.sort((a, b) => compare(a.name, b.name));
        return {kind: "object", members};
    }

    private isStringLike(type: TS.Type): boolean {
        const {ts} = this;
        if (type.flags & ts.TypeFlags.String) {
            return true;
        }

        return !!(type.flags & ts.TypeFlags.Intersection)
            && (type as TS.IntersectionType).types.some((t) => t.flags & ts.TypeFlags.String);
    }

    /** A string, a string literal, a template literal type, or `string & {}`. */
    private isStringy(type: TS.Type): boolean {
        return this.isStringLike(type) || type.isStringLiteral() || (type.flags & this.ts.TypeFlags.TemplateLiteral) !== 0;
    }

    private isFrameworkOwned(type: TS.Type): boolean {
        const symbol = type.aliasSymbol ?? type.getSymbol();
        const file = symbol?.declarations?.[0]?.getSourceFile().fileName;
        return !file || this.isFrameworkFile(file);
    }

    private isLibFile(symbol: TS.Symbol): boolean {
        return (symbol.declarations ?? []).some((d) => /[\\/]lib\.[^\\/]*\.d\.ts$/.test(d.getSourceFile().fileName));
    }

    private isFrameworkFile(fileName: string): boolean {
        if (/[\\/]typescript[\\/]lib[\\/]lib\.[^\\/]*\.d\.ts$/.test(fileName) || /[\\/]lib\.[^\\/]*\.d\.ts$/.test(fileName)) {
            return true;
        }

        const owner = packageOfFile(fileName);
        return !!owner && this.framework.includes(owner);
    }
}

// ----- probe --------------------------------------------------------------------------------------------------

/**
 * The probe lines that bring one island's export into the program. A Lit element named by its TAG (`#sl-switch`) is
 * imported for its side effect — the module that registers a tag often exports nothing — and read back through the
 * global tag map; every other export is bound to a name.
 */
function probeLines(island: IslandRequest, i: number): string {
    const module = JSON.stringify(island.module);
    if (island.runtime === "lit" && isTag(island.export)) {
        return `import ${module};\nexport type __island${i} = HTMLElementTagNameMap[${JSON.stringify(island.export)}];\n`;
    }

    return island.export === "default"
        ? `import __island${i} from ${module};\nexport { __island${i} };\n`
        : `import { ${island.export.split(".")[0]} as __island${i} } from ${module};\nexport { __island${i} };\n`;
}

/** A custom element name: lowercase, starting with a letter, with a hyphen in it. */
function isTag(name: string): boolean {
    return /^[a-z][a-z0-9._]*-[a-z0-9._-]*$/.test(name);
}

/** The probe's import that binds `__island{i}`. */
function importOf(ts: typeof TS, probe: TS.SourceFile, i: number): TS.ImportDeclaration | undefined {
    const name = `__island${i}`;
    return probe.statements.find((s): s is TS.ImportDeclaration =>
        ts.isImportDeclaration(s) && importedName(ts, s)?.text === name);
}

/** The probe node standing for `__island{i}`: its import, or — for a Lit tag — its alias over the tag map. */
function probeNode(ts: typeof TS, probe: TS.SourceFile, i: number): TS.ImportDeclaration | TS.TypeAliasDeclaration | undefined {
    const name = `__island${i}`;
    return importOf(ts, probe, i)
        ?? probe.statements.find((s): s is TS.TypeAliasDeclaration => ts.isTypeAliasDeclaration(s) && s.name.text === name);
}

/** The symbol an import binds, through its alias. */
function importedSymbol(ts: typeof TS, checker: TS.TypeChecker, declaration: TS.ImportDeclaration | undefined): TS.Symbol | undefined {
    const binding = importedName(ts, declaration);
    const resolved = binding && checker.getSymbolAtLocation(binding);
    return resolved && resolved.flags & ts.SymbolFlags.Alias ? checker.getAliasedSymbol(resolved) : resolved;
}

/** The error for an export the program could not find: a missing package, or a package without that export. */
function notFound(ts: typeof TS, program: TS.Program, island: IslandRequest, what: string): IslandError {
    return moduleResolves(ts, program, island.module)
        ? new IslandError("export-not-found", `'${island.module}' has no ${what}.`)
        : new IslandError("module-not-found",
            `'${island.module}' could not be resolved from the project — install it (npm install ${packageOf(island.module)}).`);
}

// ----- Lit ----------------------------------------------------------------------------------------------------

/**
 * A Lit element. It is a class, so its props are its public, writable instance fields — never its methods, a getter
 * alone, or a private, protected, static or readonly member — and all of them are optional. Its tag is the one the
 * global tag map gives it, as registered by the island's own module. Its events appear nowhere in its declarations; the
 * package's custom-elements manifest lists them where it ships one.
 */
function extractLit(
    ts: typeof TS,
    program: TS.Program,
    checker: TS.TypeChecker,
    island: IslandRequest,
    node: TS.ImportDeclaration | TS.TypeAliasDeclaration | undefined,
    tagMap: TS.TypeAliasDeclaration | undefined,
    probe: TS.SourceFile,
): string {
    const byTag = isTag(island.export);
    const walker = new TypeWalker(ts, checker, "lit");

    let instance: TS.Type | undefined;
    let element: TS.Symbol | undefined;
    if (node && ts.isTypeAliasDeclaration(node)) {
        instance = checker.getTypeFromTypeNode(node.type);
        element = instance.getSymbol();
    } else {
        element = importedSymbol(ts, checker, node);
        instance = element && element.flags & ts.SymbolFlags.Class ? checker.getDeclaredTypeOfSymbol(element) : undefined;
    }

    const label = `${island.module}#${island.export}`;
    if (byTag && (!element?.declarations?.length || walker.isInherited(element))) {
        // The tag map answered with lib.dom's own HTMLElement: nothing in the program registers the tag.
        if (!moduleResolves(ts, program, island.module)) {
            throw notFound(ts, program, island, "");
        }

        throw new IslandError("lit-tag-unknown",
            `'${island.module}' registers no element named '${island.export}' — import the module that defines it.`);
    }

    if (!element?.declarations?.length) {
        throw notFound(ts, program, island, island.export === "default" ? "default export" : `export named '${island.export}'`);
    }

    if (!instance) {
        throw new IslandError("not-a-component", `'${label}' is not a custom element class.`);
    }

    // Every Lit island shares one program, and so one global tag map. Only a registration the island's OWN module makes
    // counts — otherwise a class module that registers nothing would borrow the tag another island's module registers,
    // and render only when that island's chunk happened to load first.
    const files = moduleFiles(ts, checker, probe, island.module);
    if (byTag && !registers(checker, tagMap, island.export, files)) {
        throw new IslandError("lit-tag-unknown",
            `'${island.module}' does not register '${island.export}' — name the module that defines the element.`);
    }

    const tag = byTag ? island.export : tagOf(checker, tagMap, element, files);
    if (!tag) {
        throw new IslandError("lit-tag-unknown",
            `Rask cannot tell which tag '${element.getName()}' registers — name it after the '#': "${island.module}#my-element".`);
    }

    const events = manifestEvents(island.module, element, tag);
    const props: SnapshotProp[] = [];
    const skipped: Skip[] = [];

    for (const property of checker.getPropertiesOfType(instance)) {
        const name = property.getName();
        if (walker.isInherited(property) || !isPublicField(ts, property) || /^(?:[_#]|__@)/.test(name)) {
            continue;
        }

        try {
            const type = walker.serialize(checker.getTypeOfSymbol(property), 0, true);
            const doc = docOf(ts, checker, property);
            props.push({name, required: false, ...(doc ? {doc} : {}), type});
        } catch (error) {
            if (error instanceof Skipped) {
                skipped.push(error.detail ? {name, reason: error.reason, detail: error.detail} : {name, reason: error.reason});
                continue;
            }

            throw error;
        }
    }

    for (const event of events) {
        props.push({
            name: `on-${event.name}`,
            wire: `@${event.name}`,
            required: false,
            type: {kind: "callback", args: [{name: "event", type: {kind: "event", name: event.type}}]},
        });
    }

    return snapshotText("lit", island, packageInfo(island.module, element), tag, "none", props, skipped, walker);
}

/**
 * A public, writable instance field: a property, or an accessor that has a setter. Never a method, a getter alone, or
 * a member marked private, protected, static or readonly.
 */
function isPublicField(ts: typeof TS, symbol: TS.Symbol): boolean {
    const declarations = symbol.declarations ?? [];
    if (declarations.length === 0 || symbol.flags & ts.SymbolFlags.Method) {
        return false;
    }

    const hasSetter = declarations.some((d) => ts.isSetAccessorDeclaration(d));
    const hidden = ts.ModifierFlags.Private | ts.ModifierFlags.Protected | ts.ModifierFlags.Static | ts.ModifierFlags.Readonly;
    return declarations.every((d) =>
        !ts.isMethodDeclaration(d)
        && !ts.isMethodSignature(d)
        && !(ts.isGetAccessorDeclaration(d) && !hasSetter)
        && (ts.getCombinedModifierFlags(d) & hidden) === 0);
}

/** The one tag an island's own module registers for a class in HTMLElementTagNameMap, or undefined for none or several. */
function tagOf(
    checker: TS.TypeChecker,
    tagMap: TS.TypeAliasDeclaration | undefined,
    element: TS.Symbol,
    files: Set<TS.SourceFile>,
): string | undefined {
    if (!tagMap) {
        return undefined;
    }

    const tags = checker.getPropertiesOfType(checker.getTypeFromTypeNode(tagMap.type))
        .filter((entry) => declaredIn(entry, files) && checker.getTypeOfSymbol(entry).getSymbol() === element);

    return tags.length === 1 ? tags[0].getName() : undefined;
}

/** Whether the island's own module registers `tag` in HTMLElementTagNameMap. */
function registers(
    checker: TS.TypeChecker,
    tagMap: TS.TypeAliasDeclaration | undefined,
    tag: string,
    files: Set<TS.SourceFile>,
): boolean {
    const entry = tagMap && checker.getPropertyOfType(checker.getTypeFromTypeNode(tagMap.type), tag);
    return !!entry && declaredIn(entry, files);
}

function declaredIn(symbol: TS.Symbol, files: Set<TS.SourceFile>): boolean {
    return (symbol.declarations ?? []).some((d) => files.has(d.getSourceFile()));
}

/**
 * The declaration files an island's module contributes: the file its specifier resolves to, and the files that file
 * imports or re-exports directly — one step, which is how a package entry commonly pulls in the module that registers
 * its element.
 */
function moduleFiles(ts: typeof TS, checker: TS.TypeChecker, probe: TS.SourceFile, module: string): Set<TS.SourceFile> {
    const files = new Set<TS.SourceFile>();
    const fileOf = (specifier: TS.Expression | undefined) =>
        specifier ? checker.getSymbolAtLocation(specifier)?.declarations?.[0]?.getSourceFile() : undefined;

    const declaration = probe.statements.find((s): s is TS.ImportDeclaration =>
        ts.isImportDeclaration(s) && ts.isStringLiteral(s.moduleSpecifier) && s.moduleSpecifier.text === module);
    const root = fileOf(declaration?.moduleSpecifier);
    if (!root) {
        return files;
    }

    files.add(root);
    for (const statement of root.statements) {
        const referenced = ts.isImportDeclaration(statement) || ts.isExportDeclaration(statement)
            ? fileOf(statement.moduleSpecifier)
            : undefined;
        if (referenced) {
            files.add(referenced);
        }
    }

    return files;
}

/**
 * The events a package's custom-elements manifest lists for one element, following the superclass chain as far as this
 * manifest records it. Empty when the package ships no manifest or it does not list the element.
 *
 * Only events: the manifest never takes a prop away. A field Lit declares with `attribute: false` — the recommended
 * shape for arrays and objects — has no attribute in it, and a field inherited from a class in another package is not
 * in it at all; both are still properties the element reacts to.
 */
function manifestEvents(module: string, element: TS.Symbol, tag: string): {name: string; type: string}[] {
    const root = packageRoot(module, element);
    if (!root) {
        return [];
    }

    let manifest: {modules?: {declarations?: ManifestDeclaration[]}[]};
    try {
        const pkg = JSON.parse(fs.readFileSync(path.join(root, "package.json"), "utf8"));
        if (typeof pkg.customElements !== "string") {
            return [];
        }

        manifest = JSON.parse(fs.readFileSync(path.join(root, pkg.customElements), "utf8"));
    } catch {
        return [];
    }

    const declarations = (manifest.modules ?? []).flatMap((m) => m.declarations ?? []);
    const events: {name: string; type: string}[] = [];
    let declaration = declarations.find((d) => d.tagName === tag);
    for (let depth = 0; declaration && depth < 16; depth++) {
        for (const event of declaration.events ?? []) {
            // Any name addEventListener takes, `valueChanged` included; a `$` would collide with the wire's reserved keys.
            if (typeof event.name === "string" && /^[^\s$]+$/.test(event.name) && !events.some((e) => e.name === event.name)) {
                events.push({name: event.name, type: eventTypeName(event.type?.text)});
            }
        }

        const parent: string | undefined = declaration.superclass?.name;
        const current = declaration;
        declaration = parent ? declarations.find((d) => d.name === parent && d !== current) : undefined;
    }

    events.sort((a, b) => compare(a.name, b.name));
    return events;
}

interface ManifestDeclaration {
    name?: string;
    tagName?: string;
    superclass?: {name?: string};
    events?: {name?: string; type?: {text?: string}}[];
}

/** An event's class from the manifest's type text — `CustomEvent<{…}>` is a `CustomEvent` — or `CustomEvent`. */
function eventTypeName(text: unknown): string {
    const head = typeof text === "string" ? text.split("<")[0].trim() : "";
    return /^[A-Za-z_$][\w$]*$/.test(head) ? head : "CustomEvent";
}

/** The directory of the package a module belongs to, found from where its declarations were read. */
function packageRoot(module: string, symbol: TS.Symbol): string | undefined {
    const name = packageOf(module);
    const file = symbol.declarations?.[0]?.getSourceFile().fileName.replace(/\\/g, "/");
    const at = file ? file.lastIndexOf(`/node_modules/${name}/`) : -1;
    return file && at >= 0 ? `${file.substring(0, at)}/node_modules/${name}` : undefined;
}

// ----- Angular ------------------------------------------------------------------------------------------------

/**
 * An Angular component. Its inputs and outputs are declared only in the type arguments of `static ɵcmp`, which the
 * checker resolves to `unknown`, so they are read from the syntax. An input travels under its public alias, which is
 * the name Angular sets it by; an output travels as `@alias`, the adapter's cue to subscribe rather than set.
 */
function extractAngular(
    ts: typeof TS,
    program: TS.Program,
    checker: TS.TypeChecker,
    island: IslandRequest,
    declaration: TS.ImportDeclaration | undefined,
): string {
    const target = importedSymbol(ts, checker, declaration);
    if (!target?.declarations?.length) {
        throw notFound(ts, program, island, island.export === "default" ? "default export" : `export named '${island.export}'`);
    }

    const label = `${island.module}#${island.export}`;
    const statics = checker.getTypeOfSymbol(target);
    const definition = checker.getPropertyOfType(statics, "ɵcmp");
    if (!definition) {
        throw new IslandError("not-a-component", checker.getPropertyOfType(statics, "ɵdir")
            ? `'${label}' is an Angular directive, not a component.`
            : `'${label}' is not an Angular component: it carries no compiled component definition.`);
    }

    const args = definitionArguments(ts, definition);
    if (!args) {
        throw new IslandError("not-a-component", `'${label}' carries a component definition Rask cannot read.`);
    }

    if (args[7] !== true) {
        throw new IslandError("not-standalone", `'${label}' is not a standalone component; Rask mounts standalone components.`);
    }

    const instance = checker.getDeclaredTypeOfSymbol(target);
    const walker = new TypeWalker(ts, checker, "angular");
    const props: SnapshotProp[] = [];
    const skipped: Skip[] = [];

    const {inputs, outputs} = inheritedMaps(ts, checker, target);
    for (const name of Object.keys(inputs).sort(compare)) {
        const input = inputs[name];
        const alias = typeof input === "string" ? input : isRecord(input) && typeof input.alias === "string" ? input.alias : name;
        const member = checker.getPropertyOfType(instance, name);
        if (!member) {
            continue;
        }

        try {
            const type = walker.serialize(inputType(ts, checker, statics, name, checker.getTypeOfSymbol(member)), 0, true);
            const doc = docOf(ts, checker, member);
            props.push({
                name,
                ...(alias !== name ? {wire: alias} : {}),
                required: isRecord(input) && input.required === true,
                ...(doc ? {doc} : {}),
                type,
            });
        } catch (error) {
            if (error instanceof Skipped) {
                skipped.push(error.detail ? {name, reason: error.reason, detail: error.detail} : {name, reason: error.reason});
                continue;
            }

            throw error;
        }
    }

    for (const name of Object.keys(outputs).sort(compare)) {
        const alias = typeof outputs[name] === "string" && (outputs[name] as string).length > 0 ? outputs[name] as string : name;
        const member = checker.getPropertyOfType(instance, name);
        const payload = member ? emitterPayload(ts, checker, checker.getTypeOfSymbol(member)) : undefined;
        const silent = !payload || (payload.flags & (ts.TypeFlags.Void | ts.TypeFlags.Undefined)) !== 0;
        props.push({
            name: `on${alias[0].toUpperCase()}${alias.slice(1)}`,
            wire: `@${alias}`,
            required: false,
            type: {kind: "callback", args: silent ? [] : [{name: "value", type: walker.payload(payload!)}]},
        });
    }

    const content = Array.isArray(args[6]) && args[6].length > 0 ? "node" : "none";
    return snapshotText("angular", island, packageInfo(island.module, target), null, content, props, skipped, walker);
}

/**
 * The input and output maps of a component and of every class it extends, merged so the subclass wins. A compiled
 * definition lists only what its own class declares; Angular folds a base class's `ɵdir` or `ɵcmp` in at runtime
 * (`ɵɵInheritDefinitionFeature`), so an input a component inherits is settable even though its own `ɵcmp` omits it.
 */
function inheritedMaps(
    ts: typeof TS,
    checker: TS.TypeChecker,
    target: TS.Symbol,
): {inputs: Record<string, unknown>; outputs: Record<string, unknown>} {
    const chain: TS.Symbol[] = [];
    for (let symbol: TS.Symbol | undefined = target; symbol && chain.length < 16 && !chain.includes(symbol);) {
        chain.push(symbol);
        const type = checker.getDeclaredTypeOfSymbol(symbol);
        symbol = type.isClassOrInterface() ? checker.getBaseTypes(type)[0]?.getSymbol() : undefined;
    }

    // Prototype-less, like the maps they are merged from, so a "__proto__" input stays an input.
    const inputs: Record<string, unknown> = Object.create(null);
    const outputs: Record<string, unknown> = Object.create(null);
    for (const symbol of chain.reverse()) {
        // `exports` holds a class's OWN statics; a property lookup on its type would find the base's definition again.
        const own = symbol.exports?.get(ts.escapeLeadingUnderscores("ɵcmp"))
            ?? symbol.exports?.get(ts.escapeLeadingUnderscores("ɵdir"));
        const args = own && definitionArguments(ts, own);
        if (args && isRecord(args[3])) {
            Object.assign(inputs, args[3]);
        }

        if (args && isRecord(args[4])) {
            Object.assign(outputs, args[4]);
        }
    }

    return {inputs, outputs};
}

/**
 * The type an input accepts: a signal's value, a transformed input's pre-transform type, or what the component's
 * `ngAcceptInputType_x` widens a decorator input to.
 */
function inputType(ts: typeof TS, checker: TS.TypeChecker, statics: TS.Type, name: string, own: TS.Type): TS.Type {
    const kind = own.aliasSymbol?.getName() ?? own.getSymbol()?.getName();
    const args = typeArgumentsOf(ts, checker, own);
    if ((kind === "InputSignal" || kind === "ModelSignal") && args.length > 0) {
        return args[0];
    }

    if (kind === "InputSignalWithTransform" && args.length > 1) {
        return args[1].flags & ts.TypeFlags.Unknown ? args[0] : args[1];
    }

    const accept = checker.getPropertyOfType(statics, `ngAcceptInputType_${name}`);
    const accepted = accept && checker.getTypeOfSymbol(accept);
    return accepted && !(accepted.flags & ts.TypeFlags.Unknown) ? accepted : own;
}

/** What an output emits: the type argument of its EventEmitter, OutputEmitterRef, OutputRef or ModelSignal. */
function emitterPayload(ts: typeof TS, checker: TS.TypeChecker, type: TS.Type): TS.Type | undefined {
    const kind = type.aliasSymbol?.getName() ?? type.getSymbol()?.getName();
    return kind && ["EventEmitter", "OutputEmitterRef", "OutputRef", "ModelSignal"].includes(kind)
        ? typeArgumentsOf(ts, checker, type)[0]
        : undefined;
}

function typeArgumentsOf(ts: typeof TS, checker: TS.TypeChecker, type: TS.Type): readonly TS.Type[] {
    if (type.aliasTypeArguments?.length) {
        return type.aliasTypeArguments;
    }

    const reference = (type.flags & ts.TypeFlags.Object) !== 0
        && ((type as TS.ObjectType).objectFlags & ts.ObjectFlags.Reference) !== 0;
    return reference ? checker.getTypeArguments(type as TS.TypeReference) : [];
}

/**
 * The type arguments of `static ɵcmp: i0.ɵɵComponentDeclaration<…>` as plain values: strings and booleans, `null` for
 * `never`, arrays for tuples, and prototype-less objects for type literals — so a `"__proto__"` key reads as a key.
 */
function definitionArguments(ts: typeof TS, definition: TS.Symbol): unknown[] | undefined {
    const declaration = definition.declarations?.[0];
    const type = declaration && (ts.isPropertyDeclaration(declaration) || ts.isPropertySignature(declaration))
        ? declaration.type
        : undefined;
    return type && ts.isTypeReferenceNode(type) && type.typeArguments
        ? type.typeArguments.map((argument) => literalOf(ts, argument))
        : undefined;
}

function literalOf(ts: typeof TS, node: TS.TypeNode): unknown {
    if (ts.isLiteralTypeNode(node)) {
        const literal = node.literal;
        if (ts.isStringLiteral(literal)) {
            return literal.text;
        }

        return literal.kind === ts.SyntaxKind.TrueKeyword ? true : literal.kind === ts.SyntaxKind.FalseKeyword ? false : null;
    }

    if (ts.isTupleTypeNode(node)) {
        return node.elements.map((element) => literalOf(ts, ts.isNamedTupleMember(element) ? element.type : element));
    }

    if (ts.isTypeLiteralNode(node)) {
        const out: Record<string, unknown> = Object.create(null);
        for (const member of node.members) {
            if (ts.isPropertySignature(member) && member.type && (ts.isIdentifier(member.name) || ts.isStringLiteral(member.name))) {
                out[member.name.text] = literalOf(ts, member.type);
            }
        }

        return out;
    }

    return null;
}

function isRecord(value: unknown): value is Record<string, unknown> {
    return typeof value === "object" && value !== null && !Array.isArray(value);
}

// ----- events and content-------------------------------------------------------------------------------------

/**
 * Vue declares emits two ways. `defineComponent` and vue-tsc fold them into `$props` as on* handlers, which the prop
 * walk has already found. PrimeVue-style declarations leave them only as `$emit` overloads —
 * `(e: "update:pressed", ...args: [pressed: boolean]): void` — so a handler prop is synthesized for each event that is
 * not a prop already. The name keeps the event as Vue matches it (`onUpdate:pressed`); the generator makes the C# name
 * and sends this one.
 */
function synthesizeEmits(
    ts: typeof TS,
    checker: TS.TypeChecker,
    component: TS.Type,
    walker: TypeWalker,
    props: SnapshotProp[],
    skipped: Skip[],
): void {
    for (const signature of emitSignatures(ts, checker, component)) {
        const event = signature.parameters[0] && checker.getTypeOfSymbol(signature.parameters[0]);
        const names = !event ? [] : event.isUnion() ? event.types : [event];
        for (const literal of names) {
            if (!literal.isStringLiteral() || literal.value.length === 0) {
                continue;
            }

            const name = "on" + literal.value[0].toUpperCase() + literal.value.slice(1);
            if (props.some((p) => p.name === name) || skipped.some((s) => s.name === name)) {
                continue;
            }

            props.push({name, required: false, type: walker.serializeCallback(signature, 0, 1)});
        }
    }
}

/** The call signatures of a Vue component's `$emit` (on its instance) or `emit` (on a call signature's context). */
function emitSignatures(ts: typeof TS, checker: TS.TypeChecker, component: TS.Type): readonly TS.Signature[] {
    for (const construct of checker.getSignaturesOfType(component, ts.SignatureKind.Construct)) {
        const emit = checker.getReturnTypeOfSignature(construct).getProperty("$emit");
        if (emit) {
            return checker.getSignaturesOfType(checker.getTypeOfSymbol(emit), ts.SignatureKind.Call);
        }
    }

    const calls = checker.getSignaturesOfType(component, ts.SignatureKind.Call);
    const context = calls[calls.length - 1]?.parameters[1];
    const emit = context && checker.getNonNullableType(checker.getTypeOfSymbol(context)).getProperty("emit");
    return emit ? checker.getSignaturesOfType(checker.getTypeOfSymbol(emit), ts.SignatureKind.Call) : [];
}

/**
 * Svelte 4 typings declare events only as `on:` directives, which a props object cannot carry. Each is recorded as a
 * `legacy-event` skip, so the snapshot says why the component's events are missing rather than nothing at all.
 */
function legacyEvents(ts: typeof TS, checker: TS.TypeChecker, component: TS.Type, props: TS.Type, skipped: Skip[]): void {
    const events = svelteLegacyMember(ts, checker, component, props, "$$events_def", "$$events");
    if (!events) {
        return;
    }

    for (const event of checker.getPropertiesOfType(events)) {
        skipped.push({
            name: `on:${event.getName()}`,
            reason: "legacy-event",
            detail: checker.typeToString(checker.getTypeOfSymbol(event)),
        });
    }
}

/**
 * A Svelte 4 component's events or slots type: on its class instance (`$$events_def`, `$$slot_def`), or — as
 * svelte-package writes typings that are a class and a function at once — inside its props (`$$events`, `$$slots`).
 */
function svelteLegacyMember(
    ts: typeof TS,
    checker: TS.TypeChecker,
    component: TS.Type,
    props: TS.Type,
    instanceName: string,
    propsName: string,
): TS.Type | undefined {
    for (const construct of checker.getSignaturesOfType(component, ts.SignatureKind.Construct)) {
        const member = checker.getReturnTypeOfSignature(construct).getProperty(instanceName);
        if (member) {
            return checker.getNonNullableType(checker.getTypeOfSymbol(member));
        }
    }

    const member = checker.getPropertyOfType(props, propsName);
    return member ? checker.getNonNullableType(checker.getTypeOfSymbol(member)) : undefined;
}

/**
 * Whether the component takes content: a `children` that was skipped as rendered content (React, Preact, Solid, and
 * a Svelte snippet), or — where slots are declared apart from props — a `default` slot: a Svelte 4 component's, or a
 * Vue component's on its instance or its context.
 */
function contentOf(
    ts: typeof TS,
    checker: TS.TypeChecker,
    runtime: string,
    component: TS.Type,
    props: TS.Type,
    skipped: Skip[],
): string {
    if (skipped.some((s) => s.name === "children" && s.reason === "node")) {
        return "node";
    }

    if (runtime === "svelte") {
        const slots = svelteLegacyMember(ts, checker, component, props, "$$slot_def", "$$slots");
        return slots && checker.getPropertyOfType(slots, "default") ? "node" : "none";
    }

    if (runtime !== "vue") {
        return "none";
    }

    for (const construct of checker.getSignaturesOfType(component, ts.SignatureKind.Construct)) {
        const slots = checker.getReturnTypeOfSignature(construct).getProperty("$slots");
        if (slots && checker.getTypeOfSymbol(slots).getProperty("default")) {
            return "node";
        }
    }

    const calls = checker.getSignaturesOfType(component, ts.SignatureKind.Call);
    const context = calls[calls.length - 1]?.parameters[1];
    const slots = context && checker.getNonNullableType(checker.getTypeOfSymbol(context)).getProperty("slots");
    return slots && checker.getNonNullableType(checker.getTypeOfSymbol(slots)).getProperty("default") ? "node" : "none";
}

// ----- helpers ------------------------------------------------------------------------------------------------

/** A callback argument's name: its own, or `argN` where the checker has only a placeholder (`({id}) => …` is `__0`). */
function argumentName(name: string | undefined, index: number): string {
    return name && !name.startsWith("__") ? name : `arg${index}`;
}

function importedName(ts: typeof TS, declaration: TS.ImportDeclaration | undefined): TS.Identifier | undefined {
    const clause = declaration?.importClause;
    if (!clause) {
        return undefined;
    }

    if (clause.name) {
        return clause.name;
    }

    const bindings = clause.namedBindings;
    return bindings && ts.isNamedImports(bindings) ? bindings.elements[0]?.name : undefined;
}

function moduleResolves(ts: typeof TS, program: TS.Program, module: string): boolean {
    return program.getSourceFiles().some((file) => {
        const owner = packageOfFile(file.fileName);
        return owner !== undefined && owner === packageOf(module);
    });
}

/** `@mui/material/Button` → `@mui/material`; `react-colorful` → `react-colorful`. */
function packageOf(module: string): string {
    const parts = module.split("/");
    return module.startsWith("@") ? parts.slice(0, 2).join("/") : parts[0];
}

/** The package a file belongs to, from its LAST node_modules segment — which is right for pnpm's nesting too. */
function packageOfFile(fileName: string): string | undefined {
    const normalized = fileName.replace(/\\/g, "/");
    const at = normalized.lastIndexOf("/node_modules/");
    if (at < 0) {
        return undefined;
    }

    const rest = normalized.substring(at + "/node_modules/".length).split("/");
    return rest[0].startsWith("@") ? `${rest[0]}/${rest[1]}` : rest[0];
}

function packageInfo(module: string, symbol: TS.Symbol): {name: string; version: string | null} {
    const name = packageOf(module);
    const file = symbol.declarations?.[0]?.getSourceFile().fileName.replace(/\\/g, "/");
    if (file) {
        const at = file.lastIndexOf(`/node_modules/${name}/`);
        if (at >= 0) {
            try {
                const manifest = JSON.parse(fs.readFileSync(file.substring(0, at) + `/node_modules/${name}/package.json`, "utf8"));
                return {name, version: typeof manifest.version === "string" ? manifest.version : null};
            } catch {
                // No readable package.json: the version is unknown, which the snapshot says rather than guesses.
            }
        }
    }

    return {name, version: null};
}

function includesUndefined(ts: typeof TS, type: TS.Type): boolean {
    return type.isUnion() && type.types.some((t) => t.flags & (ts.TypeFlags.Undefined | ts.TypeFlags.Void));
}

function docOf(ts: typeof TS, checker: TS.TypeChecker, symbol: TS.Symbol): string | undefined {
    const text = ts.displayPartsToString(symbol.getDocumentationComment(checker))
        .replace(/\r\n?/g, "\n")
        .split("\n")
        .map((line) => line.trim())
        .join("\n")
        .trim();
    return text.length > 0 ? text : undefined;
}

function defaultOf(symbol: TS.Symbol): string | undefined {
    const tag = symbol.getJsDocTags().find((t) => t.name === "default" || t.name === "defaultValue");
    const text = tag?.text?.map((part) => part.text).join("").trim();
    return text && text.length > 0 ? text : undefined;
}

/** Ordinal comparison, so the output order never depends on the machine's locale. */
function compare(a: string, b: string): number {
    return a < b ? -1 : a > b ? 1 : 0;
}

main();
