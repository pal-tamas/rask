// Node-driven fixture for Rask.Web's runtime (src/Rask.Core/Resources/rask-api.ts, __raskWeb): what a chain's run
// answers with as data, and the patches it consults on the way. The C# test (WebRuntimeTests) runs this in a node
// subprocess and asserts the JSON on stdout.

import "./WebRuntimeWindow.js";
import "../../../src/Rask.Core/Resources/rask-api.js";

type Any = Record<string, unknown>;

interface RaskWeb {
    read(root: unknown, steps: string): Promise<unknown>;
    has(root: unknown, steps: string): boolean;
}

const web = (globalThis as unknown as { __raskWeb: RaskWeb }).__raskWeb;

// A browser's list: item(i) and length, with its items also under their indices, as a platform object's are.
class StubList {
    [index: number]: unknown;

    constructor(private readonly items: unknown[], readonly isFinal?: boolean) {
        items.forEach((item, i) => (this[i] = item));
    }

    get length(): number {
        return this.items.length;
    }

    item(index: number): unknown {
        return this.items[index];
    }
}

async function run(): Promise<Any> {
    const alternative = {transcript: "hello", confidence: 0.9};
    const results = new StubList([new StubList([alternative], true)]);
    const data = await web.read(results, "[]");

    // webkitSpeechRecognition, under its MDN name.
    Object.assign(globalThis, {webkitSpeechRecognition: class {}});
    const prefixedIsSupported = web.has(null, JSON.stringify([["g", "SpeechRecognition"]]));
    const created = await web.read(null, JSON.stringify([["n", "SpeechRecognition"]]));

    return {
        listData: data,
        prefixedIsSupported,
        prefixedCreated: created !== null && typeof created === "object",
    };
}

run().then(
    (result) => console.log(JSON.stringify(result)),
    (error) => {
        console.error(error);
        process.exit(1);
    });
