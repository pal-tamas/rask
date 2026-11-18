// Ui.Confirm.Ask: a question asked from code, in the kit's dialog, the same on both hosts. .NET calls
// `__raskConfirm.ask` by dotted name and awaits the answer: true on yes, false on every other way the dialog closes,
// null when the page has no Ui.Confirm (and the C# says what to add).
//
// In the runtime every page loads, because .NET may ask before the hooks have arrived: the dialog itself is asked in
// by rask-confirm-dialog.ts, in the hooks bundle, which a Ui.Confirm on the page is what loads (its data-rask-modal).
// So a question that comes first waits for it, and one asked on a page with no dialog is answered null at once.

import {page, seam} from "./rask-owned.js";

// How often, and for how long, a question waits for the hooks to arrive, in ms.
const LOOK = 16;
const MOST = 10000;

window.__raskConfirm = window.__raskConfirm || {
    ask: function (question: string, heading: string | null, no: string | null, yes: string | null, tone: string | null):
        Promise<boolean | null> {
        const words = {heading: heading, no: no, yes: yes, tone: tone};
        const start = performance.now();
        return new Promise(function (resolve) {
            const look = function (): void {
                if (seam.ask) {
                    seam.ask(question, words).then(resolve, function () { resolve(null); });
                    return;
                }
                const placed = page !== null && page.querySelector("[data-rask-confirm-dialog=message]") !== null;
                if (!placed || performance.now() - start >= MOST) {
                    resolve(null);
                    return;
                }
                setTimeout(look, LOOK);
            };
            look();
        });
    },
};
