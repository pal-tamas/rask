// Node-driven fixture for the parts of the runtime's behaviour hooks that are pure: how a mask shapes text and
// where it leaves the caret, which characters a one-time-code cell accepts, the safe-area triangle towards a
// submenu, and the registry of attributes a hook holds against the morph.
//
// Everything a browser has to prove — pointers, focus, the top layer — is in RuntimeHook*Tests of
// Rask.Server.E2E.Tests. The C# test (RuntimeHookShapingTests) runs this in a node subprocess and asserts
// the JSON line.

import {maskMoney, maskPattern, COPIED_MS} from "../../../src/Rask.Core/Resources/rask-field.js";
import {otpChars} from "../../../src/Rask.Core/Resources/rask-otp.js";
import {safeArea} from "../../../src/Rask.Core/Resources/rask-menu.js";
import {disown, own, ownsAttr, ownsChecked} from "../../../src/Rask.Core/Resources/rask-owned.js";
import {listsKey, rove} from "../../../src/Rask.Core/Resources/rask-keys.js";
import {scrollBy} from "../../../src/Rask.Core/Resources/rask-focus.js";
import {dragFraction, dragText} from "../../../src/Rask.Core/Resources/rask-drag.js";
import {provides} from "../../../src/Rask.Core/Resources/rask-requires.js";
import {daysIn, pastedDate, pivotYear, segmentDigit, yearDigit} from "../../../src/Rask.Core/Resources/rask-segments.js";
import {beside, nearestRow, sizeText} from "../../../src/Rask.Core/Resources/rask-plot.js";
import {progressOf} from "../../../src/Rask.Core/Resources/rask-upload.js";

const phone = "(999) 999-9999";
const a = {} as Element;
const b = {} as Element;

own(a, "data-open");
const held = ownsAttr(a, "data-open");
const otherName = ownsAttr(a, "class");
const otherElement = ownsAttr(b, "data-open");
disown(a, "data-open");
own(b, "checked");

console.log(JSON.stringify({
    copiedMs: COPIED_MS,
    pattern: {
        noisy: maskPattern("7a16x1234567890", phone),
        one: maskPattern("7", phone).value,
        three: maskPattern("716", phone).value,
        four: maskPattern("7161", phone).value,
        // "5" typed at index 3 of "(716) 123-4567": the caret was after it, at 4.
        inserted: maskPattern("(7156) 123-4567", phone, 4),
        letters: maskPattern("ab12cd", "aa-99").value,
        either: maskPattern("a1-b2!", "****").value,
        empty: maskPattern("", phone).value,
    },
    money: {
        steps: ["1", "123", "1234", "1234567", "1234567.", "1234567.8", "1234567.891"].map((t) => maskMoney(t, "").value),
        noisy: maskMoney("-12a.5.6", "").value,
        // "9" typed after the first digit of "1,234,567.89": the caret was after it, at 2.
        inserted: maskMoney("19,234,567.89", "", 2),
        european: maskMoney("1234567,891", ",.2").value,
        whole: maskMoney("1234.5", ".,0").value,
        zeros: maskMoney("007", "").value,
    },
    otp: {
        numeric: otpChars("98 76-5a4321", null, 6),
        alpha: otpChars("a1b2c3", "alpha", 6),
        alphanumeric: otpChars("a1-b2", "alphanumeric", 6),
    },
    // A flyout to the right of the pointer, and one to the left of it.
    area: {
        right: safeArea(100, 50, {left: 200, right: 400, top: 40, bottom: 140}),
        left: safeArea(300, 50, {left: 0, right: 200, top: 40, bottom: 140}),
    },
    keys: {
        listed: ["ArrowLeft", " ", "Home", "Enter", "a"].map((k) => listsKey("Arrows Space Home", k)),
        // Three radios: forward off the end, back off the start, and a group with none.
        rove: [rove(2, 1, 3), rove(0, -1, 3), rove(1, 1, 3), rove(0, 1, 0)],
        // A 100 px view at 200–300: a row above it, inside it, below it, and one taller than the view.
        scroll: [scrollBy(170, 200, 200, 300), scrollBy(230, 260, 200, 300), scrollBy(290, 320, 200, 300), scrollBy(250, 400, 200, 300)],
    },
    drag: {
        // A 220 px surface from 100 with an inset of 10: before the track, a quarter in, past its end; and no room at all.
        fractions: [dragFraction(90, 100, 220, 10), dragFraction(160, 100, 220, 10), dragFraction(400, 100, 220, 10), dragFraction(5, 0, 10, 5)],
        text: [dragText(1 / 3), dragText(2), dragText(-1)],
    },
    requires: [provides({EyeDropper: 1}, "EyeDropper"), provides({}, "EyeDropper"), provides({"a.b": 1}, "a.b"), provides({x: 1}, null)],
    progress: [progressOf(0, 0), progressOf(1, 8), progressOf(999, 1000), progressOf(5, 4)],
    plot: {
        // Rows at 0, 0.5 and 1: just before a midpoint, exactly on it, just after it; and no rows.
        nearest: [nearestRow([0, 0.5, 1], 0.24), nearestRow([0, 0.5, 1], 0.25), nearestRow([0, 0.5, 1], 0.26), nearestRow([], 0.5)],
        // An 80 px box 10 px from a point, in a 400 px frame: after it while it fits, before it when it would not.
        beside: [beside(50, 80, 10, 400), beside(310, 80, 10, 400), beside(311, 80, 10, 400)],
        size: sizeText(300.004, 99.996),
    },
    segments: {
        // A first digit: 3 can only be March and is done; 1 waits. A second digit: 12 is done, and 13 starts again at 3.
        month: [segmentDigit("month", "", "3"), segmentDigit("month", "", "1"), segmentDigit("month", "1", "2"), segmentDigit("month", "1", "3")].map((r) => r.text + (r.done ? "." : "")),
        // "00" is January for a month, and still waiting for a day.
        zeros: [segmentDigit("month", "0", "0"), segmentDigit("day", "0", "0")].map((r) => r.text + (r.done ? "." : "")),
        hour: [segmentDigit("hour", "", "2"), segmentDigit("hour", "", "3"), segmentDigit("hour", "2", "3"), segmentDigit("minute", "", "6")].map((r) => r.text + (r.done ? "." : "")),
        year: ["2", "20", "202"].reduce((all: string[], typed) => all.concat(yearDigit(typed, "6").text + (yearDigit(typed, "6").done ? "." : "")), []),
        pivot: [pivotYear("26", 2026), pivotYear("46", 2026), pivotYear("47", 2026), pivotYear("5", 2026), pivotYear("202", 2026)],
        days: [daysIn(2), daysIn(2, 2025), daysIn(2, 2024), daysIn(2, 1900), daysIn(4), daysIn(12)],
        pasted: [pastedDate("2026-03-14", ["month", "day", "year"]), pastedDate("3/14/2026", ["month", "day", "year"]), pastedDate("14.03.2026", ["day", "month", "year"]), pastedDate("13/40/2026", ["month", "day", "year"]), pastedDate("tomorrow", ["month", "day", "year"])]
            .map((d) => d ? d["year"] + "-" + d["month"] + "-" + d["day"] : null),
    },
    owned: {held, otherName, otherElement, released: !ownsAttr(a, "data-open"), checked: ownsChecked(b)},
}));
