// Ui.Editor's script: the few lines every page carries. The engine itself (Tiptap and ProseMirror, some
// 117 KB gzipped) is a separate file, fetched the first time an editor mounts and never before.

interface Engine {
    mount(root: HTMLElement, onChange: (html: string) => void): void;
    setValue(root: HTMLElement, html: string): void;
    unmount(root: HTMLElement): void;
}

let engine: Promise<Engine> | undefined;

/** Loads the engine from `src` if this page has not yet, and starts the editor under `root`. */
export async function mount(root: HTMLElement | null, src: string, onChange: (html: string) => void): Promise<void> {
    engine ??= (import(src) as Promise<Engine>).catch(error => {
        engine = undefined;
        const message = `Ui.Editor: its engine could not be loaded from ${src}. The app's build writes it to `
            + `wwwroot/js/rask-ui-editor.js: check that the host serves its static files, and that the project `
            + `does not say RaskUiEditorEngine=false. (${error})`;
        // Said here, because this is where an app's author looks: the component keeps its first paint and goes on.
        console.error(message);
        throw new Error(message);
    });
    if (root) (await engine).mount(root, onChange);
}

/** Shows `html` in the editor: a bound value the app changed. */
export async function setValue(root: HTMLElement | null, html: string): Promise<void> {
    if (root && engine) (await engine).setValue(root, html);
}

/** Takes the editor under `root` down. */
export async function unmount(root: HTMLElement | null): Promise<void> {
    if (root && engine) (await engine).unmount(root);
}
