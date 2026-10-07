// Runs Ui.AppearanceScript's inline script against a stub document, storage and media query, plays a
// scenario over it, and prints what <html> and localStorage hold afterwards.
//
//   argv[2]  the script, as the component renders it
//   argv[3]  the scenario: { stored, osDark, steps: [{ appearance | dark | os | otherTab | morph }] }
//
// UiAppearanceScriptTests runs this in a node subprocess and asserts the JSON line.
declare const process: { readonly argv: string[]; readonly stdout: { write(chunk: string): boolean } };

interface Step {
    appearance?: string;
    dark?: boolean;
    os?: boolean;
    otherTab?: string | null;
    morph?: boolean;
}

interface Scenario {
    key: string;
    stored: string | null;
    osDark: boolean;
    storageThrows?: boolean;
    steps: Step[];
}

const script = process.argv[2] ?? '';
const scenario = JSON.parse(process.argv[3] ?? '{}') as Scenario;

const classes = new Set<string>();
const attributes = new Map<string, string>();
const store = new Map<string, string>();
if (scenario.stored !== null) {
    store.set(scenario.key, scenario.stored);
}

const listeners = new Map<string, ((event: { key: string | null }) => void)[]>();
function listen(type: string, handler: (event: { key: string | null }) => void): void {
    listeners.set(type, [...(listeners.get(type) ?? []), handler]);
}

function fire(type: string, key: string | null): void {
    (listeners.get(type) ?? []).forEach((handler) => handler({key}));
}

const media = {
    matches: scenario.osDark,
    addEventListener: (_: string, handler: (event: { key: string | null }) => void) => listen('media', handler),
};

function refuse(): never {
    throw new Error('storage is unavailable');
}

const win: Record<string, unknown> = {
    matchMedia: () => media,
    addEventListener: listen,
    raskAfterMorph: () => {
        win.earlierHookRan = true;
    },
};

const doc = {
    documentElement: {
        classList: {
            toggle: (name: string, on: boolean) => (on ? classes.add(name) : classes.delete(name)),
        },
        setAttribute: (name: string, value: string) => attributes.set(name, value),
    },
};

const storage = {
    getItem: (key: string) => (scenario.storageThrows ? refuse() : store.get(key) ?? null),
    setItem: (key: string, value: string) => (scenario.storageThrows ? refuse() : store.set(key, value)),
    removeItem: (key: string) => (scenario.storageThrows ? refuse() : store.delete(key)),
};

new Function('window', 'document', 'localStorage', script)(win, doc, storage);

const rask = win.Rask as { appearance: string; dark: boolean };
for (const step of scenario.steps) {
    if (step.appearance !== undefined) {
        rask.appearance = step.appearance;
    } else if (step.dark !== undefined) {
        rask.dark = step.dark;
    } else if (step.os !== undefined) {
        media.matches = step.os;
        fire('media', null);
    } else if (step.otherTab !== undefined) {
        // Another tab wrote the key: this one hears about it and has changed nothing itself.
        if (step.otherTab === null) {
            store.delete(scenario.key);
        } else {
            store.set(scenario.key, step.otherTab);
        }

        fire('storage', scenario.key);
    } else if (step.morph) {
        // A full-document morph rewrites <html> to what the app rendered, which is neither.
        classes.clear();
        attributes.clear();
        (win.raskAfterMorph as () => void)();
    }
}

process.stdout.write(JSON.stringify({
    darkClass: classes.has('dark'),
    dataTheme: attributes.get('data-theme') ?? null,
    stored: store.get(scenario.key) ?? null,
    appearance: rask.appearance,
    dark: rask.dark,
    earlierHookRan: win.earlierHookRan === true,
}) + '\n');
