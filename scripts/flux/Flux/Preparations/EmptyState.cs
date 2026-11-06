using Microsoft.Playwright;

namespace Rask.Flux.Preparations;

/// <summary>
///     Flux's empty state, for <c>flux parity empty-state --prepare empty-state</c> (#1283). Flux has no empty state of
///     its own; its account overview demo draws one when its search matches nothing: a row whose one cell spans the table
///     (<c>colspan="2"</c>) and holds a status line. No component page shows it, so the demo is brought to it — a search
///     for what no transaction is called — and its table is set in an example frame of the docs page's size
///     (FluxParityPages' reset), out of the card, where the kit's <c>EmptyStateParity</c> stands. The card it leaves is
///     the demo's, not the table's: the bleed falls back to 1.5rem on both pages.
/// </summary>
[ParityPreparation("empty-state")]
public sealed class EmptyState : IParityPreparation
{
    /// <inheritdoc />
    public string Flux(string slug) => "demo/account-overview";

    /// <inheritdoc />
    public IReadOnlyList<string> Runtime => [];

    /// <inheritdoc />
    public async Task Prepare(IPage page, string scheme, string side)
    {
        if (side != "flux")
        {
            return;
        }

        await page.FillAsync("input[placeholder=\"Search transactions\"]", "no transaction is called this");
        await page.WaitForSelectorAsync("[data-flux-table] [role=status]", new PageWaitForSelectorOptions { Timeout = 30000 });
        await page.Run("""
            () => {
              const box = document.querySelector('[data-flux-table]').closest('[data-flux-table-bleed]');
              const wrapper = document.createElement('div');
              wrapper.setAttribute('data-preview-wrapper', '');
              wrapper.dataset.section = 'empty';
              // FluxParityPages' frame: 654px wide with 24px a side on a desktop, the screen less 25px a side on a phone.
              const phone = innerWidth < 640;
              Object.assign(wrapper.style, { padding: '64px 24px', overflowX: 'auto', boxSizing: 'border-box' },
                phone ? { width: 'auto', marginInline: '25px' } : { width: '654px', marginInline: 'auto' });
              wrapper.append(box.cloneNode(true));
              // The demo's layout goes (its body is the sidebar's grid); its ink and ground stay, as a docs page's would.
              document.body.replaceChildren(wrapper);
              document.body.style.display = 'block';
              scrollTo(0, 0);
            }
            """);
        await page.Run("() => new Promise(done => requestAnimationFrame(() => requestAnimationFrame(done)))");
    }
}
