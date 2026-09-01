// An ordinary Lit element. Nothing here imports Rask.
//
// The props type is GENERATED from LitBadge.cs. Lit's reactive properties re-render on assignment, so
// the adapter's whole update path is assigning them — there is no reconciler in between.
import { LitElement, html, css } from 'lit'
import { customElement, property } from 'lit/decorators.js'
import type { LitBadgeProps } from '@rask/LitBadge.props'

@customElement('rask-demo-badge')
export class RaskDemoBadge extends LitElement implements LitBadgeProps {
  static styles = css`
    :host { display: inline-flex; align-items: center; gap: 0.5rem; }
    .pill {
      padding: 0.125rem 0.625rem;
      border-radius: 999px;
      background: #7c3aed;
      color: #fff;
      font-size: 0.75rem;
      font-weight: 600;
    }
    .rev { font-size: 0.75rem; opacity: 0.7; font-variant-numeric: tabular-nums; }
  `

  @property({ type: String }) accessor label = ''

  @property({ type: Number }) accessor revision = 0

  render() {
    return html`
      <span class="pill" data-testid="lit-badge">${this.label}</span>
      <span class="rev">rev <b data-testid="lit-revision">${this.revision}</b></span>
    `
  }
}

// A custom element registers its own tag and nothing about the file reveals it, so the contract is
// that the module default-exports that name. Importing this module runs the registration too.
export default 'rask-demo-badge'
