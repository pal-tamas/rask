// The engine behind Ui.Editor: Tiptap (ProseMirror) wired to the markup UiEditor renders.
//
// Bundled by build.mjs into ../ui-editor.js, which an app serves as a static file and UiEditor.ts imports
// the first time an editor mounts. Nothing here knows about Rask's runtime: `mount(root)` reads the DOM it
// is given, so the same file drives a page that has no .NET behind it (the Flux parity pages).
//
// The DOM contract, as UiEditor writes it:
//   [data-ui-editor]                      the root; `data-placeholder`, and `aria-disabled="true"` locks it
//     [role=toolbar]                      buttons move focus with the arrow keys (roving tabindex)
//       button[data-editor=bold|…]        a toggle, or undo / redo
//       [data-editor=heading|align]       button[role=combobox] + [role=listbox] of [role=option][data-value]
//       [data-editor=link]                a trigger button + [popover] holding link:url, link:insert, link:unlink
//     [data-slot=content]                 the initial HTML; replaced by the editable surface

import { Editor, type AnyExtension } from '@tiptap/core';
import StarterKit from '@tiptap/starter-kit';
import Highlight from '@tiptap/extension-highlight';
import Link from '@tiptap/extension-link';
import Placeholder from '@tiptap/extension-placeholder';
import Subscript from '@tiptap/extension-subscript';
import Superscript from '@tiptap/extension-superscript';
import Table from '@tiptap/extension-table';
import TableCell from '@tiptap/extension-table-cell';
import TableHeader from '@tiptap/extension-table-header';
import TableRow from '@tiptap/extension-table-row';
import TextAlign from '@tiptap/extension-text-align';
import Underline from '@tiptap/extension-underline';

type Change = (html: string) => void;
type Init = (context: { editor: Editor }) => void;

// What a toolbar button asks the editor to do, and the name its active state is read by.
const TOGGLES: Record<string, { run: (editor: Editor) => void; active?: string }> = {
  bold: { run: e => e.chain().focus().toggleBold().run(), active: 'bold' },
  italic: { run: e => e.chain().focus().toggleItalic().run(), active: 'italic' },
  strike: { run: e => e.chain().focus().toggleStrike().run(), active: 'strike' },
  underline: { run: e => e.chain().focus().toggleUnderline().run(), active: 'underline' },
  bullet: { run: e => e.chain().focus().toggleBulletList().run(), active: 'bulletList' },
  ordered: { run: e => e.chain().focus().toggleOrderedList().run(), active: 'orderedList' },
  // Flux's blockquote button states no pressed state: it toggles, and says nothing.
  blockquote: { run: e => e.chain().focus().toggleBlockquote().run() },
  subscript: { run: e => e.chain().focus().toggleSubscript().run(), active: 'subscript' },
  superscript: { run: e => e.chain().focus().toggleSuperscript().run(), active: 'superscript' },
  highlight: { run: e => e.chain().focus().toggleHighlight().run(), active: 'highlight' },
  code: { run: e => e.chain().focus().toggleCode().run(), active: 'code' },
  undo: { run: e => e.chain().focus().undo().run() },
  redo: { run: e => e.chain().focus().redo().run() },
};

// The table extensions are installed and off, as Flux's are: `enableExtension('table')` turns one on.
const DISABLED = new Set(['table', 'tableRow', 'tableCell', 'tableHeader']);
const GAP = 5;
const mounted = new WeakMap<HTMLElement, Mounted>();

/** Turns the editor markup under `root` into a live editor. `onChange` hears every change of its HTML. */
export function mount(root: HTMLElement, onChange?: Change): void {
  if (!mounted.has(root)) mounted.set(root, new Mounted(root, onChange));
}

/** Replaces the content from outside (a bound value the app changed), without reporting it back. */
export function setValue(root: HTMLElement, html: string): void {
  mounted.get(root)?.setValue(html);
}

/** Takes the editor down and stops listening. */
export function unmount(root: HTMLElement): void {
  mounted.get(root)?.destroy();
  mounted.delete(root);
}

class Mounted {
  private readonly editor: Editor;
  private readonly stop = new AbortController();
  private readonly toolbar: HTMLElement | null;
  private silent = false;
  private open: { close: (focus?: boolean) => void } | null = null;

  constructor(private readonly root: HTMLElement, private readonly onChange?: Change) {
    const surface = root.querySelector<HTMLElement>('[data-slot=content]')!;
    const host = surface.parentElement!;
    const empty = surface.querySelector(':scope > p.is-editor-empty:only-child') !== null;
    const content = empty ? '' : surface.innerHTML;
    const attributes: Record<string, string> = {};
    for (const attribute of surface.attributes) {
      if (!['contenteditable', 'tabindex', 'translate'].includes(attribute.name)) attributes[attribute.name] = attribute.value;
    }

    this.toolbar = root.querySelector<HTMLElement>('[role=toolbar]');
    const extensions = this.extensions();
    const inits: Init[] = [];
    this.announce(extensions, inits);
    surface.remove();
    this.editor = new Editor({
      element: host,
      extensions: [...extensions.values()],
      content,
      editable: !this.disabled,
      // The kit's sheet styles the area, as Flux's does: Tiptap's own stylesheet would be a second opinion.
      injectCSS: false,
      editorProps: { attributes },
      onBeforeCreate: context => inits.forEach(init => init(context)),
      onUpdate: () => this.changed(),
      onTransaction: () => this.sync(),
    });

    // What leaves the editor is the editor's own `input` and `change`, one pair per change — not the
    // browser's, which fires for some keystrokes and not for others.
    host.addEventListener('input', event => event.stopPropagation(), { signal: this.stop.signal });
    Object.defineProperty(root, 'value', { configurable: true, get: () => this.value, set: (html: string) => this.setValue(html ?? '') });
    Object.defineProperty(root, 'editor', { configurable: true, get: () => this.editor });
    this.wireToolbar();
    this.setDisabled(this.disabled);

    // Locked and unlocked by the attribute itself, so whoever renders the root — a server patch included —
    // needs no call of its own to say so.
    const watch = new MutationObserver(() => this.setDisabled(this.disabled));
    watch.observe(root, { attributes: true, attributeFilter: ['aria-disabled'] });
    this.stop.signal.addEventListener('abort', () => watch.disconnect());
  }

  private get disabled(): boolean {
    return this.root.getAttribute('aria-disabled') === 'true';
  }

  /** The HTML an app reads: nothing at all for an empty document, as Flux answers. */
  private get value(): string {
    return this.editor.isEmpty ? '' : this.editor.getHTML();
  }

  setValue(html: string): void {
    if (html === this.value) return;
    this.silent = true;
    this.editor.commands.setContent(html, true);
    this.silent = false;
  }

  setDisabled(disabled: boolean): void {
    this.editor.setEditable(!disabled, false);
    for (const button of this.toolbar?.querySelectorAll<HTMLButtonElement>('button') ?? []) {
      if (!button.hasAttribute('data-disabled')) button.disabled = disabled;
    }
  }

  destroy(): void {
    this.open?.close();
    this.stop.abort();
    this.editor.destroy();
  }

  private extensions(): Map<string, AnyExtension> {
    const placeholder = this.root.getAttribute('data-placeholder');
    const all: AnyExtension[] = [
      StarterKit,
      Highlight,
      Link.configure({ openOnClick: false }),
      Placeholder.configure({ placeholder: placeholder as string, showOnlyWhenEditable: false }),
      Subscript,
      Superscript,
      TextAlign.configure({ types: ['paragraph', 'heading'], alignments: ['left', 'center', 'right'] }),
      Underline,
      Table,
      TableRow,
      TableCell,
      TableHeader,
    ];
    return new Map(all.map(extension => [extension.name, extension]));
  }

  // Flux's `flux:editor` event, under the kit's name: a page adds, removes or reaches into the editor's
  // extensions before it is created.
  private announce(extensions: Map<string, AnyExtension>, inits: Init[]): void {
    const off = new Set(DISABLED);
    const register = (extension: AnyExtension) => {
      extensions.set(extension.name, extension);
      off.delete(extension.name);
    };
    this.root.dispatchEvent(new CustomEvent('ui:editor', {
      bubbles: true,
      detail: {
        registerExtension: register,
        registerExtensions: (list: AnyExtension[]) => list.forEach(register),
        enableExtension: (name: string) => off.delete(name),
        disableExtension: (name: string) => off.add(name),
        init: (callback: Init) => inits.push(callback),
      },
    }));
    for (const name of off) extensions.delete(name);
  }

  private changed(): void {
    this.root.dispatchEvent(new Event('input', { bubbles: true }));
    this.root.dispatchEvent(new Event('change', { bubbles: true }));
    if (!this.silent) this.onChange?.(this.value);
  }

  // ── toolbar ────────────────────────────────────────────────────────────────────────────────────────

  private wireToolbar(): void {
    const toolbar = this.toolbar;
    if (!toolbar) return;
    const signal = this.stop.signal;

    for (const button of toolbar.querySelectorAll<HTMLButtonElement>('button[data-editor]')) {
      const toggle = TOGGLES[button.dataset.editor!];
      // The command hands focus back to the document on the next frame — Tiptap's own timing, and Flux's.
      if (toggle) button.addEventListener('click', () => toggle.run(this.editor), { signal });
    }

    for (const select of toolbar.querySelectorAll<HTMLElement>('[data-editor=heading], [data-editor=align]')) this.wireSelect(select);
    const link = toolbar.querySelector<HTMLElement>('[data-editor=link]');
    if (link) this.wireLink(link);

    // One tab stop: the control that last had focus. The arrows walk the rest, round and round.
    toolbar.addEventListener('focusin', event => {
      const stops = this.stops();
      if (!stops.includes(event.target as HTMLButtonElement)) return;
      for (const stopAt of stops) stopAt.tabIndex = stopAt === event.target ? 0 : -1;
    }, { signal });
    toolbar.addEventListener('keydown', event => {
      if (event.key !== 'ArrowRight' && event.key !== 'ArrowLeft') return;
      const stops = this.stops();
      const at = stops.indexOf(event.target as HTMLButtonElement);
      if (at < 0) return;
      stops[(at + (event.key === 'ArrowRight' ? 1 : stops.length - 1)) % stops.length].focus();
      event.preventDefault();
    }, { signal });
  }

  private stops(): HTMLButtonElement[] {
    return [...this.toolbar!.querySelectorAll<HTMLButtonElement>('button')]
      .filter(button => !button.disabled && !button.closest('[popover]') && button.getClientRects().length > 0);
  }

  private sync(): void {
    const toolbar = this.toolbar;
    if (!toolbar) return;
    for (const button of toolbar.querySelectorAll<HTMLButtonElement>('button[data-editor]')) {
      const name = TOGGLES[button.dataset.editor!]?.active;
      if (!name) continue;
      const active = this.editor.isActive(name);
      button.setAttribute('aria-pressed', String(active));
      button.toggleAttribute('data-match', active);
    }

    toolbar.querySelector('[data-editor=link] > * > button, [data-editor=link] > button')?.toggleAttribute('data-match', this.editor.isActive('link'));
    const heading = toolbar.querySelector<HTMLElement>('[data-editor=heading]');
    if (heading) this.choose(heading, [1, 2, 3].map(level => (this.editor.isActive('heading', { level }) ? `heading${level}` : '')).find(Boolean) ?? 'paragraph');
    const align = toolbar.querySelector<HTMLElement>('[data-editor=align]');
    if (align) this.choose(align, ['center', 'right'].find(side => this.editor.isActive({ textAlign: side })) ?? 'left');
  }

  // ── a select: heading, align ───────────────────────────────────────────────────────────────────────

  // Shows `value` as the select's choice: the option is marked, and its picture copied into the trigger.
  private choose(select: HTMLElement, value: string): void {
    const shown = select.querySelector<HTMLElement>('button[role=combobox] [data-value]');
    if (!shown || shown.dataset.value === value) return;
    for (const option of select.querySelectorAll<HTMLElement>('[role=option]')) {
      const selected = option.dataset.value === value;
      option.setAttribute('aria-selected', String(selected));
      option.toggleAttribute('data-selected', selected);
      if (!selected) continue;
      shown.dataset.value = value;
      shown.replaceChildren(...[...option.childNodes].map(node => node.cloneNode(true)));
    }
  }

  private apply(select: HTMLElement, value: string): void {
    const chain = this.editor.chain().focus();
    if (select.dataset.editor === 'align') chain.setTextAlign(value).run();
    else if (value === 'paragraph') chain.setParagraph().run();
    else chain.setHeading({ level: Number(value.slice(-1)) as 1 | 2 | 3 }).run();
  }

  private wireSelect(select: HTMLElement): void {
    const signal = this.stop.signal;
    const trigger = select.querySelector<HTMLButtonElement>('button[role=combobox]')!;
    const list = select.querySelector<HTMLElement>('[role=listbox]')!;
    const options = () => [...list.querySelectorAll<HTMLElement>('[role=option]')];
    const activate = (option: HTMLElement | undefined) => {
      if (!option) return;
      for (const other of options()) other.toggleAttribute('data-active', other === option);
      trigger.setAttribute('aria-activedescendant', option.id);
    };
    const close = () => {
      this.shut(list, [select, trigger, list]);
      trigger.removeAttribute('aria-activedescendant');
      for (const option of options()) option.removeAttribute('data-active');
    };
    const show = () => {
      // A press elsewhere closes the list and hands focus back to its button — after the press itself has
      // moved focus, hence the timeout.
      this.show(list, trigger, [select, trigger, list], 'start', close, () => setTimeout(() => trigger.focus()));
      list.style.width = `${trigger.offsetWidth}px`;
      activate(options().find(option => option.hasAttribute('data-selected')) ?? options()[0]);
    };
    const pick = (option: HTMLElement | undefined) => {
      close();
      if (option) this.apply(select, option.dataset.value!);
    };

    trigger.addEventListener('click', () => (list.hasAttribute('data-open') ? close() : show()), { signal });
    trigger.addEventListener('keydown', event => {
      const isOpen = list.hasAttribute('data-open');
      const all = options();
      const at = all.findIndex(option => option.hasAttribute('data-active'));
      if (event.key === 'ArrowDown' || event.key === 'ArrowUp') {
        if (!isOpen) show();
        else activate(all[Math.min(all.length - 1, Math.max(0, at + (event.key === 'ArrowDown' ? 1 : -1)))]);
      } else if (isOpen && (event.key === 'Enter' || event.key === ' ')) pick(all[at]);
      else if (isOpen && event.key === 'Escape') close();
      else return;
      event.preventDefault();
      event.stopPropagation();
    }, { signal });
    list.addEventListener('pointerover', event => activate((event.target as HTMLElement).closest<HTMLElement>('[role=option]') ?? undefined), { signal });
    // The press must not take focus from the trigger, or the editor loses the selection it is to change.
    list.addEventListener('mousedown', event => event.preventDefault(), { signal });
    list.addEventListener('click', event => {
      const option = (event.target as HTMLElement).closest<HTMLElement>('[role=option]');
      if (option) pick(option);
    }, { signal });
  }

  // ── the link popover ───────────────────────────────────────────────────────────────────────────────

  private wireLink(link: HTMLElement): void {
    const signal = this.stop.signal;
    const panel = link.querySelector<HTMLElement>(':scope > [popover]')!;
    const trigger = [...link.querySelectorAll<HTMLButtonElement>('button')].find(button => !panel.contains(button))!;
    const url = panel.querySelector<HTMLInputElement>('[data-editor="link:url"]')!;
    let byButton = false;
    const close = () => this.shut(panel, [trigger, panel]);
    // Measured on Flux: Escape gives focus back to the document when the button opened the panel, and to
    // nothing at all when the shortcut did.
    const dismiss = () => {
      close();
      if (byButton) this.editor.view.focus();
      else url.blur();
    };
    const show = (button = false) => {
      byButton = button;
      url.value = this.editor.getAttributes('link').href ?? '';
      this.show(panel, trigger, [trigger, panel], 'center', close);
      url.focus();
    };
    const insert = () => {
      const chain = this.editor.chain().focus().extendMarkRange('link');
      if (url.value) chain.setLink({ href: url.value }).run();
      else chain.unsetLink().run();
      close();
    };

    trigger.addEventListener('click', () => (panel.hasAttribute('data-open') ? close() : show(true)), { signal });
    url.addEventListener('keydown', event => {
      if (event.key === 'Enter') insert();
      else if (event.key === 'Escape') dismiss();
      else return;
      event.preventDefault();
      event.stopPropagation();
    }, { signal });
    panel.querySelector('[data-editor="link:insert"]')?.addEventListener('click', insert, { signal });
    panel.querySelector('[data-editor="link:unlink"]')?.addEventListener('click', () => {
      this.editor.chain().focus().extendMarkRange('link').unsetLink().run();
      close();
    }, { signal });
    this.editor.view.dom.addEventListener('keydown', event => {
      if (event.key.toLowerCase() !== 'k' || !(event.metaKey || event.ctrlKey) || event.shiftKey || event.altKey) return;
      event.preventDefault();
      show();
    }, { signal });
  }

  // ── popovers ───────────────────────────────────────────────────────────────────────────────────────

  // Opens `panel` under `anchor`, in the top layer, and closes it on a press elsewhere, a scroll or a resize.
  private show(panel: HTMLElement, anchor: HTMLElement, marked: HTMLElement[], align: 'start' | 'center', close: () => void, pressedOutside?: () => void): void {
    this.open?.close();
    for (const element of marked) element.setAttribute('data-open', '');
    anchor.setAttribute('aria-expanded', 'true');
    panel.showPopover();
    const box = anchor.getBoundingClientRect();
    const left = align === 'start' ? box.left : box.left + (box.width - panel.offsetWidth) / 2;
    const below = box.bottom + GAP;
    const top = below + panel.offsetHeight > window.innerHeight && box.top - GAP - panel.offsetHeight >= 0 ? box.top - GAP - panel.offsetHeight : below;
    Object.assign(panel.style, {
      position: 'absolute',
      overflowY: 'auto',
      inset: `${top + window.scrollY}px auto auto ${Math.max(0, left) + window.scrollX}px`,
    });

    const away = new AbortController();
    const outside = (event: Event) => {
      if (panel.contains(event.target as Node) || anchor.contains(event.target as Node)) return;
      close();
      pressedOutside?.();
    };
    document.addEventListener('pointerdown', outside, { signal: away.signal, capture: true });
    window.addEventListener('resize', close, { signal: away.signal });
    window.addEventListener('scroll', event => { if (!panel.contains(event.target as Node)) close(); }, { signal: away.signal, capture: true });
    this.open = { close: () => { away.abort(); close(); } };
    (panel as HTMLElement & { away?: AbortController }).away = away;
  }

  private shut(panel: HTMLElement, marked: HTMLElement[]): void {
    const held = panel as HTMLElement & { away?: AbortController };
    held.away?.abort();
    held.away = undefined;
    this.open = null;
    for (const element of marked) element.removeAttribute('data-open');
    marked.find(element => element.hasAttribute('aria-expanded'))?.setAttribute('aria-expanded', 'false');
    if (panel.matches(':popover-open')) panel.hidePopover();
    panel.removeAttribute('style');
  }
}
