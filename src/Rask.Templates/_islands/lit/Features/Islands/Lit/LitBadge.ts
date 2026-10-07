// An ordinary Lit element. Lit needs no bundler plugin — this is plain TypeScript — but it still
// pairs with a .cs by filename, which is what tells Rask it is an island rather than scoped script.
//
// Written without decorators on purpose: lowering `@customElement` is the bundler's job, and an
// element whose decorator was not lowered never upgrades — the island renders empty with nothing in
// the console. `static properties` plus `customElements.define` is the same API with no transform.
import { LitElement, css, html } from 'lit'
import type { LitBadgeProps } from '@rask/LitBadge.props'

class LitBadge extends LitElement implements LitBadgeProps {
  static properties = {
    step: { type: Number },
    caption: { type: String },
    onTotal: { attribute: false },
    total: { state: true },
  }

  static styles = css`
    :host { display: inline-flex; align-items: center; gap: 0.5rem; }
  `

  // `declare`, never an initializer: a class field would shadow the getter and setter Lit installs
  // for each reactive property, and assigning it would stop re-rendering.
  declare step: number
  declare caption: string
  declare onTotal?: (total: number) => void
  private declare total: number

  constructor() {
    super()
    this.step = 1
    this.caption = ''
    this.total = 0
  }

  private add() {
    this.total += this.step
    this.onTotal?.(this.total)
  }

  render() {
    return html`
      <span class="caption">${this.caption}</span>
      <button type="button" data-testid="lit-badge-add" @click=${this.add}>add ${this.step}</button>
      <strong data-testid="lit-badge-total">${this.total}</strong>
    `
  }
}

customElements.define('lit-badge', LitBadge)

// A Lit-runtime module default-exports its registered tag name: a custom element registers its own
// tag and nothing else about the file reveals it.
export default 'lit-badge'
