// Node-driven fixture for rask-confirm.ts — whether an action that asks first (`.Confirm`) may go.
//
// The browser's dialogs are stood in for by a window whose confirm() and prompt() answer what the fixture says
// and remember what they were asked. The C# test (ConfirmClientTests) runs this in a node subprocess and
// asserts the JSON line.

class Stub {
    clicks = 0;
    readonly submittedBy: unknown[] = [];
    readonly isConnected = true;

    constructor(readonly attributes: Record<string, string>) {
    }

    getAttribute(name: string): string | null { return name in this.attributes ? this.attributes[name] : null; }

    click(): void { this.clicks++; }

    requestSubmit(by?: unknown): void { this.submittedBy.push(by ?? null); }
}

const asked: string[] = [];
let answer: boolean | string | null = true;
const page = globalThis as unknown as Record<string, unknown>;
page.window = globalThis;
page.confirm = function (question: string) { asked.push("confirm:" + question); return answer === true; };
page.prompt = function (question: string) { asked.push("prompt:" + question); return answer; };

const {confirmed, submitConfirmed} = await import("../../../src/Rask.Core/Resources/rask-confirm.js");
const el = (attributes: Record<string, string>) => new Stub(attributes) as unknown as Element;

const plain = confirmed(el({}));
const askedOfPlain = asked.length;

answer = true;
const accepted = confirmed(el({"data-rask-confirm": "Delete the partner?"}));
answer = false;
const dismissed = confirmed(el({"data-rask-confirm": "Delete the partner?"}));

const prompt = el({"data-rask-confirm": "Type the name", "data-rask-confirm-expect": "Acme"});
answer = "Acme";
const typedExactly = confirmed(prompt);
answer = "acme";
const typedInAnotherCase = confirmed(prompt);
answer = null;
const promptCancelled = confirmed(prompt);

asked.length = 0;
answer = false;
const form = el({"data-rask-confirm": "Send the order?"});
const button = el({"data-rask-confirm": "Really?"});
const formSaidNo = submitConfirmed(form, button);
const askedWhenTheFormSaidNo = asked.slice();
asked.length = 0;
answer = true;
const bothSaidYes = submitConfirmed(form, button);
const askedWhenBothSaidYes = asked.slice();
const noSubmitter = submitConfirmed(el({}), null);

// With Ui.Confirm on the page the hook answers through the seam: the browser is not asked, and a yes makes the
// action again — the same press, or the same submit by the same button.
type Again = () => void;
const seam = (globalThis as unknown as {__raskHookSeam: Record<string, unknown>}).__raskHookSeam;
const inDialog: string[] = [];
const agains: Again[] = [];
seam.confirm = function (_el: Element, question: string, expect: string | null, again: Again) {
    inDialog.push(question + "|" + expect);
    agains.push(again);
    return false;
};
asked.length = 0;
const press = new Stub({"data-rask-confirm": "Delete it?", "data-rask-confirm-expect": "Acme"});
const pressWaitsForTheDialog = confirmed(press as unknown as Element);
agains[0]();
const pressedAgain = press.clicks;
const order = new Stub({"data-rask-confirm": "Send the order?"});
const sender = new Stub({});
const submitWaitsForTheDialog = submitConfirmed(order as unknown as Element, sender as unknown as Element);
agains[1]();
const resubmittedBySameButton = order.submittedBy.length === 1 && order.submittedBy[0] === sender;
const browserAskedWithDialog = asked.slice();
seam.confirm = function () { return null; };
answer = true;
const withoutDialogBrowserAsks = confirmed(el({"data-rask-confirm": "No dialog here?"}));
const browserAskedWithoutDialog = asked.slice();
delete seam.confirm;

process.stdout.write(JSON.stringify({
    inDialog,
    pressWaitsForTheDialog,
    pressedAgain,
    submitWaitsForTheDialog,
    resubmittedBySameButton,
    browserAskedWithDialog,
    withoutDialogBrowserAsks,
    browserAskedWithoutDialog,
    plain,
    askedOfPlain,
    accepted,
    dismissed,
    typedExactly,
    typedInAnotherCase,
    promptCancelled,
    formSaidNo,
    askedWhenTheFormSaidNo,
    bothSaidYes,
    askedWhenBothSaidYes,
    noSubmitter,
}) + "\n");
