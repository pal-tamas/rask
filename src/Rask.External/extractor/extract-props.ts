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
    const probeText = islands
        .map((island, i) => island.export === "default"
            ? `import __island${i} from ${JSON.stringify(island.module)};\nexport { __island${i} };\n`
            : `import { ${island.export.split(".")[0]} as __island${i} } from ${JSON.stringify(island.module)};\nexport { __island${i} };\n`)
        .join("");

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

    const imports = probe.statements.filter(ts.isImportDeclaration);
    return islands.map((island, i) => {
        try {
            const snapshot = extractIsland(ts, program, checker, runtime, island, imports[i]);
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

    props.sort((a, b) => compare(a.name, b.name));
    skipped.sort((a, b) => compare(a.name, b.name));

    const snapshot: Record<string, unknown> = {
        schema: 1,
        runtime,
        module: island.module,
        export: island.export,
        package: packageInfo(island.module, target),
        tag: null,
        content: contentOf(ts, checker, runtime, componentType, propsType, skipped),
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

// ----- events and content -------------------------------------------------------------------------------------

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
