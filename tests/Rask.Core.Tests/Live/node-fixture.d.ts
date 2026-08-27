// What the Node fixtures use of Node itself, declared narrowly.
//
// There is no `node_modules` here and there is not going to be one: fetching `@types/node` would
// mean npm in a build that deliberately has none, for four members. So this states the four.
//
// Same principle the framework applies to third-party JS elsewhere — declare what you call, not what
// exists. If a fixture starts needing more of Node, add it here rather than reaching for the package.

declare const process: {
    /** The bundled fixture's own path is argv[1]; anything the C# test passes follows. */
    readonly argv: string[];

    readonly stdout: {
        /** The fixtures report a single JSON line, which their C# test parses. */
        write(chunk: string): boolean;
    };

    /** Non-zero means the stub itself failed, which is distinct from an assertion failing in C#. */
    exit(code: number): never;
};
