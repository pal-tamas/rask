// Script fixture for a link that asks first — `Ui.MenuItem.Href("/logout").Confirm("…")`: rask-clicks.ts asks before
// the browser or the host follows it, through rask-confirm.ts.
//
// The browser's confirm() is stood in for, and a link is a stub whose click() is a fresh press through the same
// guard, as the browser's would be. The C# test (LinkConfirmClientTests) runs this on Jint and asserts what it reports.

import {report} from "../../Rask.TestSupport/ScriptFixtures/fixture.js";

class StubElement {
}

const asked: string[] = [];
let answer = true;
const page = globalThis as unknown as Record<string, unknown>;
page.window = globalThis;
page.Element = StubElement;
page.confirm = function (question: string) { asked.push(question); return answer; };

const {setHost} = await import("../../../src/Rask.Core/Resources/rask-host.js");
setHost({send: () => undefined, inRoot: () => true});
const {navLinkClick} = await import("../../../src/Rask.Core/Resources/rask-clicks.js");

type Press = { target: unknown; button: number; defaultPrevented: boolean; preventDefault(): void };

function press(target: unknown): Press {
    return {
        target, button: 0, defaultPrevented: false,
        preventDefault() { this.defaultPrevented = true; },
    };
}

class Link extends StubElement {
    readonly tagName = "A";
    readonly isConnected = true;
    // Each press the browser would follow: one the guard let through, its default left alone.
    followed = 0;

    constructor(readonly attributes: Record<string, string>) {
        super();
    }

    getAttribute(name: string): string | null { return name in this.attributes ? this.attributes[name] : null; }

    hasAttribute(name: string): boolean { return name in this.attributes; }

    // Only the two selectors the guard asks: a link that asks, and an in-app link.
    closest(selector: string): Link | null {
        const asks = selector.indexOf("data-rask-confirm") >= 0;
        const wanted = asks ? "data-rask-confirm" : "data-rask-nav";
        return this.hasAttribute(wanted) && this.hasAttribute("href") ? this : null;
    }

    click(): void {
        const again = press(this);
        navLinkClick(again as unknown as MouseEvent);
        if (!again.defaultPrevented) this.followed++;
    }
}

const settled = () => new Promise<void>(function (resolve) { setTimeout(resolve, 400); });

// Whether a press on `link` is followed: at once, or by the press a yes makes again once the page caught up.
async function follows(link: Link): Promise<{ now: boolean; later: number }> {
    const first = press(link);
    navLinkClick(first as unknown as MouseEvent);
    const now = !first.defaultPrevented;
    await settled();
    return {now, later: link.followed};
}

const plain = await follows(new Link({href: "/logout"}));
const askedOfPlain = asked.length;

answer = false;
const refused = await follows(new Link({href: "/logout", "data-rask-confirm": "Sign out?"}));
const askedWhenRefused = asked.slice();

asked.length = 0;
answer = true;
const accepted = await follows(new Link({href: "/logout", "data-rask-confirm": "Sign out?"}));
const askedWhenAccepted = asked.slice();

asked.length = 0;
const handled = await follows(new Link({href: "/logout", "data-rask-confirm": "Sign out?", "data-rask-on-click": "h1"}));
const askedOfHandled = asked.length;

report({
    plainNow: plain.now,
    askedOfPlain,
    refusedNow: refused.now,
    refusedLater: refused.later,
    askedWhenRefused,
    acceptedNow: accepted.now,
    acceptedLater: accepted.later,
    askedWhenAccepted,
    handledNow: handled.now,
    askedOfHandled,
});
