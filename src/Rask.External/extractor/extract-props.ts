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
};

/** Type names that are rendered content, not data — a child, a slot, an element. */
const NodeTypeNames = new Set([
    "ReactNode", "ReactElement", "ReactPortal", "Element", "JSXElement", "ComponentChildren", "ComponentChild",
    "VNode", "Children", "Snippet",
]);

/** Type names that are refs, which have no meaning across the wire. */
const RefTypeNames = new Set(["Ref", "RefObject", "RefCallback", "LegacyRef", "ForwardedRef", "MutableRefObject"]);

const MaxDepth = 4;

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
            : `import { ${island.export} as __island${i} } from ${JSON.stringify(island.module)};\nexport { __island${i} };\n`)
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
        customConditions: runtime === "solid" ? ["solid"] : undefined,
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

    const componentType = checker.getTypeOfSymbolAtLocation(target, binding);
    const propsType = propsOf(ts, checker, componentType);
    if (!propsType) {
        throw new IslandError("not-a-component",
            `'${island.module}${island.export === "default" ? "" : "#" + island.export}' is not a ${runtime} component: it has no call signature taking props.`);
    }

    const walker = new TypeWalker(ts, checker, runtime);
    const props: SnapshotProp[] = [];
    const skipped: Skip[] = [];

    for (const {symbol: property, required} of propertiesOf(ts, checker, propsType)) {
        const name = property.getName();
        if (name === "key" || name === "ref") {
            continue;
        }

        if (name.startsWith("$")) {
            skipped.push({name, reason: "unsupported", detail: "a name starting with '$' is reserved by the props wire"});
            continue;
        }

        if (walker.isInherited(property)) {
            continue;
        }

        const propertyType = checker.getTypeOfSymbol(property);
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
                skipped.push(error.detail ? {name, reason: error.reason, detail: error.detail} : {name, reason: error.reason});
                continue;
            }

            throw error;
        }
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
        content: skipped.some((s) => s.name === "children" && s.reason === "node") ? "node" : "none",
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
 * The props type of a component value: the first parameter of its last non-generic call signature (covers plain
 * functions, memo, forwardRef, and MUI's OverridableComponent, whose default-props overload comes last), or the
 * instance `props` of a class component.
 */
function propsOf(ts: typeof TS, checker: TS.TypeChecker, component: TS.Type): TS.Type | undefined {
    const calls = checker.getSignaturesOfType(component, ts.SignatureKind.Call);
    const call = [...calls].reverse().find((s) => !s.typeParameters?.length) ?? calls[calls.length - 1];
    if (call && call.parameters.length > 0) {
        return checker.getTypeOfSymbol(call.parameters[0]);
    }

    const constructs = checker.getSignaturesOfType(component, ts.SignatureKind.Construct);
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
function propertiesOf(ts: typeof TS, checker: TS.TypeChecker, props: TS.Type): {symbol: TS.Symbol; required: boolean}[] {
    const seen = new Map<string, {symbol: TS.Symbol; required: boolean; members: number}>();
    const constituents = props.isUnion() ? props.types : [props];
    for (const type of constituents) {
        for (const property of checker.getPropertiesOfType(checker.getApparentType(type))) {
            const required = (property.flags & ts.SymbolFlags.Optional) === 0
                && !includesUndefined(ts, checker.getTypeOfSymbol(property));
            const entry = seen.get(property.getName());
            if (entry) {
                entry.required = entry.required && required;
                entry.members++;
            } else {
                seen.set(property.getName(), {symbol: property, required, members: 1});
            }
        }
    }

    return [...seen.values()].map((entry) => ({
        symbol: entry.symbol,
        required: entry.required && entry.members === constituents.length,
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

        const alias = type.aliasSymbol?.getName() ?? type.getSymbol()?.getName();
        if (alias && NodeTypeNames.has(alias) && this.isFrameworkOwned(type)) {
            throw new Skipped("node");
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

        const literals = rest.filter((t) => t.isStringLiteral() || t.isNumberLiteral() || t.flags & ts.TypeFlags.BooleanLiteral);
        const others = rest.filter((t) => !literals.includes(t));

        if (literals.length > 0) {
            // Literals plus an open `string` (or `string & {}`): still an enum, but one the package extends.
            const opensWithString = others.length > 0 && others.every((t) => this.isStringLike(t));
            if (others.length === 0 || opensWithString) {
                const values = literals.map((t) =>
                    t.isStringLiteral() ? t.value : t.isNumberLiteral() ? t.value : this.checker.typeToString(t) === "true");
                const base = values.every((v) => typeof v === "number") ? "number" : "string";
                const sorted = [...new Set(values)].sort((a, b) =>
                    typeof a === "number" && typeof b === "number" ? a - b : compare(String(a), String(b)));
                const result: SnapshotType = {kind: "enum", base, values: sorted};
                if (opensWithString) {
                    result.open = true;
                }

                return withNull(result);
            }
        }

        const kinds = rest.map((t) => this.serialize(t, depth, top));
        const distinct = [...new Map(kinds.map((k) => [JSON.stringify(k), k])).values()];
        if (distinct.length === 1) {
            return withNull(distinct[0]);
        }

        // Ties broken by content: members of one kind otherwise stay in the checker's type-id order, which moves
        // with whatever else the shared program checked first — and a committed snapshot must not.
        distinct.sort((a, b) => compare(a.kind, b.kind) || compare(JSON.stringify(a), JSON.stringify(b)));
        return withNull({kind: "union", of: distinct});
    }

    private serializeCallback(signature: TS.Signature, depth: number): SnapshotType {
        const {ts, checker} = this;
        const args = signature.parameters.map((parameter) => {
            const declaration = parameter.valueDeclaration as TS.ParameterDeclaration | undefined;
            const optional = !!declaration && (!!declaration.questionToken || !!declaration.initializer);
            const type = checker.getTypeOfSymbol(parameter);
            return {
                name: parameter.getName(),
                ...(optional ? {optional: true} : {}),
                type: this.argument(type, depth),
            };
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

// ----- helpers ------------------------------------------------------------------------------------------------

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
