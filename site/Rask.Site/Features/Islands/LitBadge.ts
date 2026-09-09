// A plain custom element, used as an island. Nothing here imports Rask, and nothing imports a
// framework either — this is the runtime with no dependency at all.
//
// Deliberately written WITHOUT decorators. `@customElement` and `accessor` are standard-decorator
// syntax that the bundler has to lower, and Vite's oxc-based transform does not: the chunk builds,
// ships and loads, and the element simply never upgrades — an island that renders empty with nothing
// in the console. `customElements.define` plus ordinary setters has no transform to depend on.
//
// The props type is GENERATED from LitBadge.cs, so renaming a C# property stops this compiling.
import type { LitBadgeProps } from '@rask/LitBadge.props'

class RaskLitBadge extends HTMLElement {
  // The adapter assigns each prop as a PROPERTY, one at a time, on mount and on every update. So each
  // one re-renders rather than waiting for a batch that never comes.
  #label: LitBadgeProps['label'] = ''
  #value: LitBadgeProps['value'] = 0
  #onNudged: LitBadgeProps['onNudged']

  // State the element owns and C# never sees. A prop change must not reset it: the adapter assigns
  // onto this live node instead of replacing it, which is what "reconcile, not remount" means for a
  // runtime that has no reconciler.
  #nudges = 0

  set label(value: string) {
    this.#label = value
    this.#render()
  }

  set value(value: number) {
    this.#value = value
    this.#render()
  }

  set onNudged(value: LitBadgeProps['onNudged']) {
    this.#onNudged = value
  }

  connectedCallback(): void {
    this.#render()
  }

  #render(): void {
    if (!this.isConnected) {
      return
    }

    if (this.childElementCount === 0) {
      this.append(this.#build())
    }

    this.#refresh()
  }

  #build(): HTMLElement {
    const root = document.createElement('div')
    root.className = 'lit-badge'
    root.dataset['testid'] = 'lit-badge'

    const caption = document.createElement('span')
    caption.dataset['testid'] = 'lit-label'
    root.append(caption)

    const reading = document.createElement('strong')
    reading.dataset['testid'] = 'lit-value'
    root.append(reading)

    const button = document.createElement('button')
    button.type = 'button'
    button.dataset['testid'] = 'lit-nudge'
    button.textContent = 'nudge'
    button.addEventListener('click', () => {
      this.#nudges += 1
      this.#refresh()
      this.#onNudged?.(this.#nudges)
    })
    root.append(button)

    const nudges = document.createElement('span')
    nudges.dataset['testid'] = 'lit-nudges'
    root.append(nudges)

    return root
  }

  #refresh(): void {
    this.#text('lit-label', this.#label)
    this.#text('lit-value', String(this.#value))
    this.#text('lit-nudges', String(this.#nudges))
  }

  #text(testId: string, value: string): void {
    const node = this.querySelector(`[data-testid="${testId}"]`)
    if (node !== null) {
      node.textContent = value
    }
  }
}

customElements.define('site-lit-badge', RaskLitBadge)

// A Lit-runtime module default-exports its registered tag name: a custom element registers its own
// tag and nothing else about the file reveals it.
export default 'site-lit-badge'
