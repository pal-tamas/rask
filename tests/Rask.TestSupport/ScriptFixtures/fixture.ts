// What a *Fixture.ts can ask of the .NET test that runs it (ScriptFixture.Run): its arguments, one report, a file
// read, in-memory modules to import, and the next task. Everything else a fixture uses is the web platform (see
// web-globals.js), or a stub it writes.

interface FixtureHost {
    readonly args: readonly string[];
    report(json: string): void;
    readText(path: string): string;
    addModule(path: string, source: string): void;
    setTimer(callback: () => void, delay: number, repeat: boolean): number;
}

declare const __raskFixture: FixtureHost;

/** What the C# test passed after the fixture's name. */
export const args: readonly string[] = __raskFixture.args;

/**
 * Hands `value`, as JSON, back to the C# test — once, and as the last thing the fixture does: the run ends with the
 * task that reports.
 */
export function report(value: unknown): void {
    __raskFixture.report(JSON.stringify(value));
}

/** A file's text, as UTF-8 — the built bundle a fixture drives, say. */
export function readText(path: string): string {
    return __raskFixture.readText(path);
}

/**
 * Makes `source` importable at `path`, ahead of any file there: a shipped script stood up beside stubs of the
 * modules it imports, with no temporary directory to create or clean up.
 */
export function addModule(path: string, source: string): void {
    __raskFixture.addModule(path, source);
}

/**
 * Resolves in the next task, once every microtask queued so far has run. On the host's own timers, so it still
 * comes when a fixture has replaced the page's setTimeout with a clock of its own.
 */
export function nextTask(): Promise<void> {
    return new Promise((done) => {
        __raskFixture.setTimer(done, 0, false);
    });
}
