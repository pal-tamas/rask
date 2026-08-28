// Monaco (MIT), vendored under wwwroot/lib/monaco/vs. This describes the slice PlaygroundView.ts
// drives and nothing else.
//
// Hand-written rather than taken from the `monaco-editor` package, because there is no node_modules
// here and vendoring ~30k lines of upstream typings to use twenty members of them would be a poor
// trade. The rule is that everything declared here is something this file actually calls: a narrow
// declaration that is true is worth more than a complete one that drifts, since the compiler
// believes either one equally.

declare namespace monaco {
    /** A position in the buffer. Monaco counts lines and columns from 1, not 0. */
    interface IPosition {
        readonly lineNumber: number;
        readonly column: number;
    }

    interface IRange {
        startLineNumber: number;
        startColumn: number;
        endLineNumber: number;
        endColumn: number;
    }

    /** The word around a position, used to place a completion's replacement range. */
    interface IWordAtPosition {
        readonly word: string;
        readonly startColumn: number;
        readonly endColumn: number;
    }

    enum MarkerSeverity {
        Hint = 1,
        Info = 2,
        Warning = 4,
        Error = 8,
    }

    /** Bit flags, which is why the caller combines them with `|`. */
    enum KeyMod {
        CtrlCmd = 2048,
    }

    enum KeyCode {
        Enter = 3,
    }

    namespace editor {
        interface ITextModel {
            getValue(): string;
            getOffsetAt(position: IPosition): number;
            getWordUntilPosition(position: IPosition): IWordAtPosition;
        }

        interface IMarkerData {
            severity: MarkerSeverity;
            message: string;
            startLineNumber: number;
            startColumn: number;
            endLineNumber: number;
            endColumn: number;
        }

        interface IStandaloneEditorConstructionOptions {
            value?: string;
            language?: string;
            theme?: string;
            automaticLayout?: boolean;
            minimap?: { enabled: boolean };
            scrollBeyondLastLine?: boolean;
            fontSize?: number;
            tabSize?: number;
            renderLineHighlight?: string;
            fixedOverflowWidgets?: boolean;
        }

        interface IStandaloneCodeEditor {
            getValue(): string;
            setValue(value: string): void;
            setScrollTop(top: number): void;
            getModel(): ITextModel | null;
            addCommand(keybinding: number, handler: () => void): void;
            onDidChangeModelContent(listener: () => void): void;
        }

        function create(
            host: HTMLElement,
            options?: IStandaloneEditorConstructionOptions): IStandaloneCodeEditor;

        /** @param owner namespaces the marker set, so setting ours never clears somebody else's. */
        function setModelMarkers(model: ITextModel | null, owner: string, markers: IMarkerData[]): void;
    }

    namespace languages {
        enum CompletionItemKind {
            Method = 0,
            Function = 1,
            Constructor = 2,
            Field = 3,
            Variable = 4,
            Class = 5,
            Struct = 6,
            Interface = 7,
            Module = 8,
            Property = 9,
            Event = 10,
            Operator = 11,
            Unit = 12,
            Value = 13,
            Constant = 14,
            Enum = 15,
            EnumMember = 16,
            Keyword = 17,
            Text = 18,
            Color = 19,
            File = 20,
            Reference = 21,
            Customcolor = 22,
            Folder = 23,
            TypeParameter = 24,
            User = 25,
            Issue = 26,
            Snippet = 27,
        }

        interface CompletionItem {
            label: string;
            kind: CompletionItemKind;
            insertText: string;
            sortText?: string;
            detail?: string;
            range: IRange;
        }

        interface CompletionList {
            suggestions: CompletionItem[];
        }

        interface CompletionItemProvider {
            triggerCharacters?: string[];
            provideCompletionItems(
                model: editor.ITextModel,
                position: IPosition): CompletionList | Promise<CompletionList>;
        }

        function registerCompletionItemProvider(
            languageId: string,
            provider: CompletionItemProvider): void;
    }
}

/** Monaco's AMD loader, which `loader.js` installs on the global object. */
interface MonacoRequire {
    config(options: { paths: Record<string, string> }): void;
    (modules: string[], onLoad: () => void, onError?: (reason: unknown) => void): void;
}

/** How Monaco finds its web worker. Read by the loader, set before requiring the editor. */
interface MonacoEnvironmentShape {
    baseUrl?: string;
    getWorkerUrl?(): string;
}

declare var MonacoEnvironment: MonacoEnvironmentShape | undefined;

/**
 * The .NET WASM runtime's own accessor, used to read the boot config.
 *
 * Typed loosely on purpose: the resource-group shape has changed across .NET versions (arrays of
 * `{name}`, or name-to-hash maps), and the reader below is written to tolerate all of them. A
 * precise type here would be a guess that goes stale on the next runtime bump.
 */
interface DotnetRuntimeApi {
    getConfig?(): { resources?: Record<string, unknown> } | null;
}

declare function getDotnetRuntime(index: number): DotnetRuntimeApi | null;
