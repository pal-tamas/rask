// An ordinary Lit element. Lit needs no bundler plugin — this is plain TypeScript — but it still
// pairs with a .cs by filename, which is what tells Rask it is an island rather than scoped script.
import { LitElement, css, html } from 'lit'
import { customElement, property, state } from 'lit/decorators.js'
import type { LitBadgeProps } from '@rask/LitBadge.props'

@customElement('lit-badge')
export class LitBadge extends LitElement implements LitBadgeProps {
  static styles = css`
    :host { display: inline-flex; align-items: center; gap: 0.5rem; }
  `

  // The legacy decorator form on purpose: `accessor` requires experimentalDecorators:false, Angular
  // requires it true, and one project may hold both islands. This form works under either.
  @property({ type: Number }) step = 1
  @property({ type: String }) caption = ''
  @property({ attribute: false }) onTotalChanged?: (total: number) => void

  @state() private total = 0

  private add() {
    this.total += this.step
    this.onTotalChanged?.(this.total)
  }

  render() {
    return html`
      <span class="caption">${this.caption}</span>
      <button type="button" data-testid="lit-badge-add" @click=${this.add}>add ${this.step}</button>
      <strong data-testid="lit-badge-total">${this.total}</strong>
    `
  }
}
