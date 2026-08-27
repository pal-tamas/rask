// What the WASM Node fixtures use of Node itself, declared narrowly.
//
// There is no `node_modules` here and there is not going to be one: fetching `@types/node` would
// mean npm in a build that deliberately has none, for nine members across four modules. So this
// states the nine.
//
// Same principle the framework applies to third-party JS elsewhere — declare what you call, not what
// exists. If a fixture starts needing more of Node, add it here rather than reaching for the package.
//
// Deliberately not shared with tests/Rask.Core.Tests/Live/node-fixture.d.ts. Those fixtures need
// only `process`; a shared file would grow to the union of both and stop describing either.

declare const process: {
    /** The bundled fixture's own path is argv[1]; the C# test's arguments follow. */
    readonly argv: string[];

    readonly stdout: {
        /** The fixtures report a single JSON line, which their C# test parses. */
        write(chunk: string): boolean;
    };

    /** Non-zero means the stub itself failed, which is distinct from an assertion failing in C#. */
    exit(code: number): never;
};

declare module "node:fs" {
    export function readFileSync(path: string, encoding: "utf8"): string;
    export function writeFileSync(path: string, data: string): void;
    export function copyFileSync(source: string, destination: string): void;
    export function mkdirSync(path: string, options?: { recursive?: boolean }): void;

    /** Returns the created directory's path, which is the prefix plus six random characters. */
    export function mkdtempSync(prefix: string): string;
}

declare module "node:os" {
    export function tmpdir(): string;
}

declare module "node:path" {
    export function join(...segments: string[]): string;
}

declare module "node:url" {
    /**
     * A dynamic `import()` of an absolute path needs a file:// URL on Windows, where a bare path is
     * read as a protocol.
     */
    export function pathToFileURL(path: string): URL;
}
