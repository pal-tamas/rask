// Shared diff-codec interpreter consumed by both rask.js (Server) and
// rask.wasm.js (WASM). Concatenated into each runtime at build time — see the
// MSBuild "_RaskBuildClientJs" target in Rask.Server.csproj and
// "_RaskSpliceClientJs" in Rask.Wasm.csproj (they splice this file at the
// RASK_DOM marker).
//
// Why concat instead of import / network split (same rationale as rask-morph.js):
//  - rask.js is a classic <script> served from /rask/rask.js (no ES-module hook).
//  - rask.wasm.js is loaded by JSHost.ImportAsync as an ES module.
// Concat sidesteps the loader mismatch and keeps the single-file delivery model.
//
// Modern JS is fine here (current-browser targets), with the same two splice
// constraints as rask-morph.js: the top-level helpers stay hoisted `function`
// declarations — applyDiff calls reviveScript() and raskShouldSuppressValue()
// (both defined in rask-morph.js, spliced into the same scope) regardless of
// splice order — and no `export` / `import` (this island is spliced inside the
// Server's classic-script IIFE, where module syntax is illegal).

// ----- Diff codec interpreter --------------------------------------------
// Applies ops produced by C#-side FrameDiffer.Diff to the live DOM. Each op
// names its target via a Path = sequence of childNodes indices from `document`.
// The Path is computed by the diff walker counting only DOM-relevant frames
// (Element, Text, Raw, Doctype) and excluding Attribute frames, which matches
// the browser's `Node.childNodes` collection semantics for the rendered HTML.
//
// Each op is a positional array; the kind at op[0] selects which trailing slots
// are present (mirrors LivePayload.BuildPayloadUtf8Diff exactly):
//   1 SetAttribute     [k, path, name|idx, value]
//   2 RemoveAttribute  [k, path, name|idx]
//   3 UpdateText       [k, path, value]
//   4 InsertSubtree    [k, path, html, domCount]
//   5 RemoveSubtree    [k, path, domCount]
//   6 MoveSubtree      [k, path, sourceSlot]
//   7 PermutationBatch [k, parentPath, moves]
//   8 MorphSubtree     [k, path, innerHtml]
//
// Names for SetAttribute/RemoveAttribute may arrive as either a string (inline) or
// a number that indexes into the optional payload-level "names" array — the server
// interns names that appear 2+ times in the same payload to drop the duplicate
// string bytes. resolveName() handles either form.
// Comment nodes shift childNodes indices relative to the server's frame walk.
// Filter to DOM-relevant nodes only (Element=1, Text=3, Doctype=10) so paths
// match what FrameDiffer counts.
const _relevantNodeTypes = {1: 1, 3: 1, 10: 1};

function relevantChild(parent, index) {
    if (!parent || !parent.childNodes) return null;
    let seen = 0;
    for (const n of parent.childNodes) {
        if (_relevantNodeTypes[n.nodeType]) {
            if (seen === index) return n;
            seen++;
        }
    }
    return null;
}

// Like relevantChild but counts as if `skip` were already gone — the post-detach
// coordinate the keyed differ uses for move targets. Lets us resolve the anchor
// WITHOUT detaching the moving node, so the move can run as a single relocation.
function relevantChildSkipping(parent, index, skip) {
    if (!parent || !parent.childNodes) return null;
    let seen = 0;
    for (const n of parent.childNodes) {
        if (n === skip) continue;
        if (_relevantNodeTypes[n.nodeType]) {
            if (seen === index) return n;
            seen++;
        }
    }
    return null;
}

// Relocate `node` before `ref` under `parent`. Prefer the Atomic Move API
// (moveBefore, Chromium 133+): it moves the node WITHOUT disconnecting it, so a
// focused descendant keeps its focus, selection, and caret across a keyed reorder.
// removeChild+insertBefore — and even a bare insertBefore — disconnect the node
// and blur it, which silently broke the "survivors keep their DOM state" contract.
// Fall back to insertBefore where moveBefore is unavailable or rejects the move.
function moveChildBefore(parent, node, ref) {
    if (parent.moveBefore) {
        try {
            parent.moveBefore(node, ref);
            return;
        } catch (e) {
            // Not connected / cross-document — fall through to insertBefore.
        }
    }
    parent.insertBefore(node, ref);
}

function resolvePath(path) {
    let node = document;
    for (const slot of path) {
        node = relevantChild(node, slot);
        if (!node) return null;
    }
    return node;
}

// Mirror selected attribute writes onto the matching IDL property. After user
// interaction, an input's `value` attribute is the *default*, not the current
// state — setAttribute does not reach the live value. Same for `checked` on
// checkboxes/radios and `selected` on options. Only sync when the element
// supports the property so we don't silently no-op on unrelated tags.
//
// Active-element guard: when the diff would overwrite the value of the focused
// input, the server's view is racing with the user's keystrokes (the server
// rendered with a value computed before the latest key landed). Skipping the
// sync on the focused element keeps the user's in-flight typing intact; the
// next keystroke updates server state and any subsequent render reconciles.
function syncFormProperty(el, name, value, isPresent) {
    // `isPresent` tells us whether the attribute is set or being removed —
    // separate from the value because the HTML attributes `checked`/`selected`
    // are presence-based: `<input checked>`, `<input checked="">`, and
    // `<input checked="checked">` all mean checked. RemoveAttribute → unchecked.
    if (!el) return;
    const tag = el.tagName;
    if (!tag) return;
    if (name === "value" && (tag === "INPUT" || tag === "TEXTAREA" || tag === "SELECT")) {
        if (document.activeElement === el) return;
        if (raskShouldSuppressValue(el, value)) return;
        el.value = value;
    } else if (name === "checked" && tag === "INPUT") {
        if (raskShouldSuppressChecked(el, !!isPresent)) return;
        el.checked = !!isPresent;
    } else if (name === "selected" && tag === "OPTION") {
        el.selected = !!isPresent;
    }
}

function applyDiff(ops, names) {
    function resolveName(raw) {
        // Server interns names that repeat 2+ times in the same payload — those
        // arrive as integer indices into the "names" array. Strings pass through.
        if (typeof raw === "number" && names) return names[raw];
        return raw;
    }

    // Symmetric with the discard below: tag any foreign head node injected before this diff (still pending,
    // not yet delivered to the async observer) so the end-of-diff discard only drops the framework's own
    // head insertions, never a coincidentally-pending library injection.
    _raskTagForeignHeadNodes();

    for (const op of ops) {
        const k = op[0];
        const path = op[1] || [];
        switch (k) {
            case 1: { // SetAttribute [k, path, name|idx, value]
                const el = resolvePath(path);
                if (el && el.setAttribute) {
                    const name1 = resolveName(op[2]);
                    const rawVal = op[3];
                    const newVal = rawVal == null ? "" : rawVal;
                    el.setAttribute(name1, newVal);
                    // After a form-control has been interacted with, the value
                    // attribute is desynchronised from the .value/.checked property
                    // (the attribute is the *default*, not the current state). Sync
                    // the IDL property too so user-visible state matches the diff.
                    syncFormProperty(el, name1, newVal, true);
                }
                break;
            }
            case 2: { // RemoveAttribute [k, path, name|idx]
                const el2 = resolvePath(path);
                if (el2 && el2.removeAttribute) {
                    const name2 = resolveName(op[2]);
                    el2.removeAttribute(name2);
                    syncFormProperty(el2, name2, "", false);
                }
                break;
            }
            case 3: { // UpdateText [k, path, value]
                const textNode = resolvePath(path);
                if (textNode) {
                    // UpdateText only ever targets a Text node now: the diff codec emits it
                    // exclusively for changed Text frames (HTML-encoded content), so
                    // .textContent is the correct knob. A changed Raw frame is NOT an
                    // UpdateText — its verbatim markup parses into a variable run of DOM
                    // nodes that textContent would escape and could not fully replace, so the
                    // codec ships it as a Remove+Insert that routes to the full-HTML morph.
                    const txtVal = op[2];
                    textNode.textContent = txtVal == null ? "" : txtVal;
                }
                break;
            }
            case 4: { // InsertSubtree [k, path, html, domCount]
                const insertHtml = op[2];
                if (typeof insertHtml !== "string") {
                    console.warn("[Rask] InsertSubtree without payload — server " +
                        "must include HTML fragment. Falling back to full reload.");
                    location.reload();
                    return;
                }
                const parentPath = path.slice(0, path.length - 1);
                const slot = path[path.length - 1];
                const parent = resolvePath(parentPath);
                if (!parent) break;
                const template = document.createElement("template");
                template.innerHTML = insertHtml;
                // Scripts parsed via innerHTML carry the "already started" flag and will
                // NOT execute when inserted into the live document. Rebuild them via
                // reviveScript so a scoped <script src="/_rask/a/{hash}.js"> (or a user
                // Head <script>) delivered through a keyed InsertSubtree diff actually
                // runs — otherwise its window.Rask.{Type}/global never appears. Mirrors
                // the full-HTML morph path, which already revives inserted scripts.
                for (const oldScript of template.content.querySelectorAll("script")) {
                    oldScript.parentNode.replaceChild(reviveScript(oldScript), oldScript);
                }
                const refNode = parent.childNodes[slot] || null;
                while (template.content.firstChild) {
                    parent.insertBefore(template.content.firstChild, refNode);
                }
                break;
            }
            case 5: { // RemoveSubtree [k, path, domCount]
                const rmParentPath = path.slice(0, path.length - 1);
                const rmSlot = path[path.length - 1];
                const rmParent = resolvePath(rmParentPath);
                if (!rmParent) break;
                const removeCount = op[2] || 1;
                for (let r = 0; r < removeCount; r++) {
                    const victim = rmParent.childNodes[rmSlot];
                    if (!victim) break;
                    rmParent.removeChild(victim);
                }
                break;
            }
            case 6: { // MoveSubtree [k, path, sourceSlot]
                // Path encodes parent + destination slot; op[2] is the source slot.
                // The destination slot is in the server's post-detach coordinate
                // (the live DOM with the moved node removed), so resolve the anchor
                // by SKIPPING the moving node rather than detaching it — then relocate
                // with moveChildBefore so a focused descendant keeps focus/selection.
                const mvParentPath = path.slice(0, path.length - 1);
                const mvDst = path[path.length - 1];
                const mvParent = resolvePath(mvParentPath);
                if (!mvParent) break;
                const mvSrcRaw = op[2];
                const mvSrc = mvSrcRaw == null ? 0 : mvSrcRaw;
                const mvNode = relevantChild(mvParent, mvSrc);
                if (!mvNode) break;
                const mvRef = relevantChildSkipping(mvParent, mvDst, mvNode);
                moveChildBefore(mvParent, mvNode, mvRef);
                break;
            }
            case 7: { // PermutationBatch [k, parentPath, moves] — moves = [dst0,src0,dst1,src1,…]
                // path IS the parent (no trailing slot to split off). Replay each (dst,src)
                // pair in array order: the server computed every pair against the live DOM
                // as mutated by the preceding pairs, so order is load-bearing — never reorder.
                // Each dst is a post-detach slot, so resolve the anchor by skipping the moving
                // node and relocate with moveChildBefore (preserves focus across the reorder).
                const pbParent = resolvePath(path);
                if (!pbParent) break;
                const pbMoves = op[2] || [];
                for (let m = 0; m + 1 < pbMoves.length; m += 2) {
                    const pbDst = pbMoves[m];
                    const pbSrc = pbMoves[m + 1];
                    const pbNode = relevantChild(pbParent, pbSrc);
                    if (!pbNode) continue;
                    const pbRef = relevantChildSkipping(pbParent, pbDst, pbNode);
                    moveChildBefore(pbParent, pbNode, pbRef);
                }
                break;
            }
            case 8: { // MorphSubtree [k, path, innerHtml]
                // The Raw-tainted fallback, scoped: reconcile the CHILDREN of the element at `path`
                // against fresh inner HTML via the same morph() the full-document path uses — but
                // localised to this one subtree the server could still address by a clean path. A Raw's
                // markup expands into an unknown DOM-node count, so the server can't emit reliable
                // positional child ops here; a morph reparses it correctly and preserves keyed / focus /
                // IDL state on everything it doesn't need to touch (incl. the rest of the document).
                const msEl = resolvePath(path);
                if (!msEl) break;
                // Shallow-clone the ACTUAL parent (not a generic <template>) so innerHTML parses in the
                // element's own context — correct for <table>/<select>/<tr>/… children. The clone carries
                // msEl's current attributes (already reconciled by any SetAttribute ops applied before
                // this one), so morph sees them matching and only touches the children.
                const model = msEl.cloneNode(false);
                model.innerHTML = op[2] == null ? "" : op[2];
                morph(msEl, model);
                break;
            }
            default:
                // Unknown op kind — newer server, older client. Bail to full reload
                // so the user isn't stranded on a stale tree.
                console.warn("[Rask] Unknown diff op kind: " + k);
                location.reload();
                return;
        }
    }
    // A diff can insert Head-declared <script>/<link> into <head> (keyed InsertSubtree). Discard those
    // framework mutations from the head observer's queue so they aren't tagged as foreign injections
    // (see _raskHeadObserver in rask-morph.js).
    _raskDiscardFrameworkHeadMutations();
}

// ----- Frame jsInvokes dispatch ------------------------------------------
// The IJSRuntime calls a render frame carried (reply.jsInvokes) run HERE — after applyDiff/morph
// has patched the DOM — so each acts on the committed DOM (e.g. focus a <dialog> that just gained
// its `open` attribute). Both clients call this right after applying the body; only the per-invoke
// executor differs per host (Server posts the result over the WS; WASM returns it through the
// endInvokeJSResult JSExport), so the caller passes dispatchOne. Shared so the loop isn't copied.
function applyFrameInvokes(reply, dispatchOne) {
    const invokes = reply && reply.jsInvokes;
    if (!invokes || typeof invokes.length !== "number") return;
    for (const inv of invokes) {
        if (inv && typeof inv.identifier === "string") dispatchOne(inv);
    }
}

// ----- Focus trap (data-rask-focus-trap) ---------------------------------
// Generic accessible-overlay focus management, driven declaratively so any overlay (Rask.Bootstrap's
// BsModal, or your own) opts in with a single attribute. For as long as an element carrying
// data-rask-focus-trap is in the DOM:
//   * focus moves into it on appear (the [autofocus] element, else the element itself), remembering
//     what had focus so it can be restored on close;
//   * Tab / Shift+Tab cycle within it — focus can't escape to the inert page behind;
//   * Escape closes it by clicking its own / a descendant [data-rask-dismiss] control (a real Rask
//     click handler), so there is no per-keystroke server round-trip;
//   * focus returns to the previously-focused element when the trap leaves the DOM.
// A single document MutationObserver tracks appear/disappear (works with the diff morph that adds and
// removes the overlay); keydown is handled at capture so it fires wherever focus currently sits.
(function installRaskFocusTrap() {
    if (typeof document === "undefined" || typeof MutationObserver === "undefined"
        || window.__raskFocusTrap) {
        return;
    }
    window.__raskFocusTrap = true;

    // No escaped quotes in this selector on purpose: the WASM client-JS splice mangles a backslash in a
    // spliced body, so the negative-tabindex exclusion is done in focusables() via el.tabIndex instead of
    // a [tabindex="-1"] attribute selector (which also correctly excludes tabindex=-1 on any element).
    const FOCUSABLE = "a[href],area[href],button:not([disabled]),"
        + "input:not([disabled]):not([type=hidden]),select:not([disabled]),textarea:not([disabled]),"
        + "[tabindex],[contenteditable=true]";

    let currentTrap = null;
    let restoreTo = null;

    // The topmost trap in the DOM (last in document order) wins when several are open (stacked modals).
    function activeTrap() {
        const traps = document.querySelectorAll("[data-rask-focus-trap]");
        return traps.length ? traps[traps.length - 1] : null;
    }

    function focusables(trap) {
        return Array.prototype.filter.call(
            trap.querySelectorAll(FOCUSABLE),
            (el) => el.tabIndex >= 0
                && (el.offsetWidth > 0 || el.offsetHeight > 0 || el === document.activeElement));
    }

    function enter(trap) {
        // Focus the [autofocus] element if the author marked one, else the trap itself (it carries
        // tabindex=-1 so screen readers announce the dialog). Deferred to rAF so the just-morphed-in
        // element is laid out before we move focus.
        const target = trap.querySelector("[autofocus]") || trap;
        requestAnimationFrame(function () {
            try {
                target.focus();
            } catch (e) {
                // element may have been removed again already
            }
        });
    }

    function restore() {
        const el = restoreTo;
        restoreTo = null;
        if (el && typeof el.focus === "function") {
            try {
                el.focus();
            } catch (e) {
                // previously-focused element is gone
            }
        }
    }

    function sync() {
        const trap = activeTrap();
        if (trap === currentTrap) {
            return;
        }

        if (!currentTrap && trap) {
            restoreTo = document.activeElement; // first trap opened over the page
        }

        currentTrap = trap;
        if (trap) {
            enter(trap);
        } else {
            restore(); // last trap closed
        }
    }

    document.addEventListener("keydown", function (e) {
        const trap = currentTrap;
        if (!trap) {
            return;
        }

        if (e.key === "Escape") {
            const dismiss = trap.hasAttribute("data-rask-dismiss")
                ? trap
                : trap.querySelector("[data-rask-dismiss]");
            if (dismiss) {
                e.preventDefault();
                dismiss.click();
            }
            return;
        }

        if (e.key !== "Tab") {
            return;
        }

        const items = focusables(trap);
        if (!items.length) {
            e.preventDefault(); // nothing to move to — keep focus off the page behind
            return;
        }

        const first = items[0];
        const last = items[items.length - 1];
        const active = document.activeElement;
        if (e.shiftKey && (active === first || active === trap || !trap.contains(active))) {
            e.preventDefault();
            last.focus();
        } else if (!e.shiftKey && (active === last || !trap.contains(active))) {
            e.preventDefault();
            first.focus();
        }
    }, true);

    // Only re-scan when a mutation actually adds or removes a trap (or a subtree containing one), so the
    // observer stays cheap on the frequent unrelated morphs.
    function touchesTrap(nodes) {
        for (let i = 0; i < nodes.length; i++) {
            const n = nodes[i];
            if (n.nodeType === 1
                && (n.matches("[data-rask-focus-trap]") || n.querySelector("[data-rask-focus-trap]"))) {
                return true;
            }
        }
        return false;
    }

    const observer = new MutationObserver(function (records) {
        for (let i = 0; i < records.length; i++) {
            if (touchesTrap(records[i].addedNodes) || touchesTrap(records[i].removedNodes)) {
                sync();
                return;
            }
        }
    });
    observer.observe(document.documentElement, { childList: true, subtree: true });
    sync(); // a trap already present at load
})();

// ----- Overflow-escaping popover (data-rask-popover) ---------------------
// The Popper-less .dropdown-menu components (BsDatePicker/BsTimePicker/BsDateTimePicker, BsDropdown,
// BsMultiSelect) render their menu as position:absolute inside a .dropdown wrapper, so any ancestor
// with overflow:hidden/auto (a card, a scroll region) clips it — the menu opens but is cut off. This
// helper re-anchors an open menu with position:fixed + viewport-computed coordinates, which resolves
// against the viewport and so escapes every overflow-clipping ancestor. A component opts in by marking
// its .dropdown wrapper with data-rask-popover and its trigger with data-rask-anchor; while the
// wrapper's .dropdown-menu carries .show the menu is placed below the trigger (flipping above when it
// doesn't fit), clamped into the viewport, right-aligned when data-rask-popover-align="end". A single
// document MutationObserver watches the .show class toggle (the menus persist in the DOM), and
// capture-phase scroll + resize keep the menu pinned to the trigger.
//
// Caveat: position:fixed only escapes overflow when NO ancestor establishes a fixed containing block
// (a non-none transform / filter / perspective / backdrop-filter / will-change of those, or contain:
// paint/layout/strict/content). Inside such an ancestor the menu is clamped to that box instead of the
// viewport — a browser rule, not a Rask bug. Selectors here carry no escaped quotes/backslashes (the
// WASM client-JS splice mangles them, as noted on the focus trap above).
(function installRaskPopover() {
    if (typeof document === "undefined" || typeof MutationObserver === "undefined"
        || window.__raskPopover) {
        return;
    }
    window.__raskPopover = true;

    const GAP = 2;      // px between the trigger and the menu
    const MARGIN = 8;   // min px kept between the menu and every viewport edge
    const Z = 1000;     // above the components' fixed click-outside backdrop (z-index 999)

    // Every opted-in wrapper that currently has an open (.show) menu, paired with that menu.
    function openMenus() {
        const pairs = [];
        const wraps = document.querySelectorAll("[data-rask-popover]");
        for (let i = 0; i < wraps.length; i++) {
            const menu = wraps[i].querySelector(".dropdown-menu.show");
            if (menu) {
                pairs.push({ wrap: wraps[i], menu: menu });
            }
        }
        return pairs;
    }

    function anchorOf(wrap) {
        return wrap.querySelector("[data-rask-anchor]")
            || wrap.querySelector(".dropdown-toggle")
            || wrap.firstElementChild;
    }

    function place(wrap, menu) {
        const anchor = anchorOf(wrap);
        if (!anchor) {
            return;
        }
        // Clear our own height cap before measuring so the menu's natural size drives placement — else
        // each reposition would feed the previous frame's cap back in. (Width is pinned and stable below,
        // so it needs no such reset.)
        menu.style.maxHeight = "";
        // Measure BEFORE switching to fixed: a menu sized with w-100 (BsMultiSelect) still reports the
        // trigger width here, but would stretch to the viewport once position:fixed — so pin that width.
        const a = anchor.getBoundingClientRect();
        const m = menu.getBoundingClientRect();
        const natH = menu.scrollHeight; // full content height, unaffected by the maxHeight we apply below
        const vw = document.documentElement.clientWidth;
        const vh = document.documentElement.clientHeight;
        const alignEnd = wrap.getAttribute("data-rask-popover-align") === "end";

        // Vertical: below by default; flip above only when it doesn't fit below and there is more room up.
        const roomBelow = vh - a.bottom - GAP - MARGIN;
        const roomAbove = a.top - GAP - MARGIN;
        let top = (natH <= roomBelow || roomBelow >= roomAbove)
            ? a.bottom + GAP
            : a.top - GAP - natH;
        if (top < MARGIN) {
            top = MARGIN;
        }

        // Horizontal: align to the trigger's start (or end), then clamp into the viewport.
        let left = alignEnd ? (a.right - m.width) : a.left;
        if (left + m.width > vw - MARGIN) {
            left = vw - MARGIN - m.width;
        }
        if (left < MARGIN) {
            left = MARGIN;
        }

        menu.style.position = "fixed";
        menu.style.margin = "0";
        menu.style.zIndex = "" + Z;
        // Pin with !important priority: a w-100 menu (BsSelect/BsMultiSelect) carries Bootstrap's
        // .w-100 { width: 100% !important }, which a plain inline width can't beat — so once the menu is
        // position:fixed the 100% would resolve against the viewport (the initial containing block) and
        // stretch it viewport-wide. An inline !important outranks the class !important, pinning the width
        // we measured while it was still position:absolute (== the trigger width). reset() clears it.
        menu.style.setProperty("width", m.width + "px", "important");
        menu.style.left = left + "px";
        menu.style.top = top + "px";
        // Cap the height to the space between the menu top and the viewport bottom and scroll internally,
        // so a menu taller than the viewport (a long list, a calendar on a short window) stays fully
        // reachable instead of overflowing off-screen — a fixed element can't be revealed by page scroll
        // the way the old position:absolute menu could.
        menu.style.maxHeight = (vh - top - MARGIN) + "px";
        menu.style.overflowY = "auto";
        // A fixed menu no longer clips together with its trigger, so hide it while the trigger is scrolled
        // entirely out of the viewport rather than leaving it floating detached over unrelated content.
        menu.style.visibility =
            (a.bottom <= 0 || a.top >= vh || a.right <= 0 || a.left >= vw) ? "hidden" : "";
    }

    // Return a closed menu to its normal in-flow (position:absolute) rendering.
    function reset(menu) {
        menu.style.position = "";
        menu.style.margin = "";
        menu.style.zIndex = "";
        menu.style.width = "";
        menu.style.left = "";
        menu.style.top = "";
        menu.style.maxHeight = "";
        menu.style.overflowY = "";
        menu.style.visibility = "";
    }

    // Re-place every open menu; returns how many were open so callers can track whether any remain.
    function reposition() {
        const pairs = openMenus();
        for (let i = 0; i < pairs.length; i++) {
            place(pairs[i].wrap, pairs[i].menu);
        }
        return pairs.length;
    }

    // True while at least one popover menu is open. Kept so the observer can cheaply skip idle morphs
    // (no open menu, nothing popover-related changed) without a document query.
    let hasOpen = false;

    // Coalesce the high-frequency scroll/resize path to one run per animation frame so a burst doesn't
    // thrash layout (each place() reads geometry then writes styles).
    let scheduled = false;
    function scheduleReposition() {
        if (scheduled) {
            return;
        }
        scheduled = true;
        requestAnimationFrame(function () {
            scheduled = false;
            hasOpen = reposition() > 0;
        });
    }

    // Scroll doesn't bubble, but a capture-phase listener on window still receives it from any ancestor
    // scroller, so the menu tracks the trigger when a card / scroll region (not just the page) scrolls.
    window.addEventListener("scroll", scheduleReposition, true);
    window.addEventListener("resize", scheduleReposition);

    // Does a mutation batch touch a popover (a menu's class toggled, or a subtree add/remove containing
    // one)? Used only to detect the open transition when nothing was open before.
    function touchesPopover(nodes) {
        for (let i = 0; i < nodes.length; i++) {
            const n = nodes[i];
            if (n.nodeType === 1
                && (n.matches("[data-rask-popover],.dropdown-menu")
                    || n.querySelector("[data-rask-popover],.dropdown-menu"))) {
                return true;
            }
        }
        return false;
    }

    // On the open transition, move focus into the menu's [autofocus] element (the searchable BsSelect's
    // filter input) so the user can type immediately — Rask only auto-focuses [autofocus] inside a
    // data-rask-focus-trap (modal), which a plain dropdown is not. Idempotent via __raskOpen so a
    // re-render that rewrites the still-open menu's class doesn't steal focus back on every keystroke.
    // Deferred to rAF so the just-morphed-in field is laid out before we focus it. A menu with no
    // [autofocus] (the date/time pickers) keeps focus on its editable trigger — no change.
    function onOpen(wrap, menu) {
        if (menu.__raskOpen) {
            return;
        }
        menu.__raskOpen = true;
        const af = menu.querySelector("[autofocus]");
        if (!af) {
            return;
        }
        menu.__raskReturn = anchorOf(wrap) || null; // where to send focus back on close
        requestAnimationFrame(function () {
            try {
                af.focus();
            } catch (e) {
                // field removed again already
            }
        });
    }

    // On close, return focus to the trigger (like a native <select>) so keyboard flow continues from the
    // box — but only when we had moved focus into the filter, and only if focus is still loose (on <body>
    // because the filter was removed, or anywhere inside the wrapper), never yanking focus the user moved.
    function onClose(wrap, menu) {
        if (!menu.__raskOpen) {
            return;
        }
        menu.__raskOpen = false;
        const ret = menu.__raskReturn;
        menu.__raskReturn = null;
        if (ret) {
            const ae = document.activeElement;
            if (ae === document.body || (wrap.contains && wrap.contains(ae))) {
                try {
                    ret.focus();
                } catch (e) {
                    // trigger gone (component unmounted)
                }
            }
        }
    }

    // The live-diff morph reconciles each element's attributes back to the rendered output, and the
    // rendered menu carries no inline style — so ANY re-render of a component with an open menu strips the
    // fixed positioning we wrote (an unrelated style-attribute write the class-only observer never sees).
    // So while a menu is open we must re-place after every morph batch, not only when the menu node itself
    // changed — hence `hasOpen` in the gate. When nothing is open and nothing popover-related changed, the
    // gate skips the document query entirely, so idle live-diff churn stays free. Runs synchronously (in
    // the mutation microtask) so a just-opened menu is fixed before anything can read it as absolute.
    const observer = new MutationObserver(function (records) {
        let touched = false;
        for (let i = 0; i < records.length; i++) {
            const r = records[i];
            if (r.type === "attributes") {
                const t = r.target;
                if (t.nodeType === 1 && t.classList && t.classList.contains("dropdown-menu")) {
                    touched = true;
                    const pop = t.closest("[data-rask-popover]");
                    if (pop) {
                        if (t.classList.contains("show")) {
                            onOpen(pop, t); // just opened — focus its [autofocus] filter
                        } else {
                            reset(t);       // just closed — drop the fixed inline styles
                            onClose(pop, t);
                        }
                    }
                }
            } else if (touchesPopover(r.addedNodes) || touchesPopover(r.removedNodes)) {
                touched = true;
            }
        }
        if (touched || hasOpen) {
            hasOpen = reposition() > 0;
        }
    });
    observer.observe(document.documentElement,
        { subtree: true, childList: true, attributes: true, attributeFilter: ["class"] });

    // While a popover is open, suppress the NATIVE side-effects of the combobox navigation/commit keys so
    // they act only inside the dropdown — most importantly Enter, which in the filter <input> would
    // otherwise fire the surrounding <form>'s implicit submit (validating the whole form) instead of just
    // picking the highlighted option. We only preventDefault, never stopPropagation: the C# keydown
    // handler is dispatched on the document bubble phase (rask-events.js), so the event must still reach
    // it to select / navigate / close. Printable keys, Space and Left/Right are left alone so typing into
    // the filter — and moving the text caret in the editable date/time picker box — keeps working; the
    // picker's day cursor still moves on Left/Right (its C# handler runs regardless). Capture-phase so we
    // run before the browser commits the default action.
    const CONTAIN = ["Enter", "Escape", "ArrowUp", "ArrowDown", "Home", "End", "PageUp", "PageDown"];
    document.addEventListener("keydown", function (e) {
        if (!hasOpen || CONTAIN.indexOf(e.key) < 0) {
            return;
        }
        const wrap = (e.target && e.target.closest) ? e.target.closest("[data-rask-popover]") : null;
        if (wrap && wrap.querySelector(".dropdown-menu.show")) {
            e.preventDefault();
        }
    }, true);

    hasOpen = reposition() > 0; // a menu already open at load
})();

// ----- Recovery affordance (data-rask-reload) ----------------------------
// A click on any element carrying data-rask-reload reloads the page. Used by the default error page so a
// user stranded on an uncaught fault has an in-app way back without hunting for the browser's reload.
// Delegated + CSP-clean (no inline handler); a no-op if the runtime never loaded (the browser's own
// reload remains the ultimate fallback).
(function installRaskReload() {
    if (typeof document === "undefined" || typeof document.addEventListener !== "function"
        || typeof window === "undefined" || window.__raskReload) {
        return;
    }
    window.__raskReload = true;
    document.addEventListener("click", function (e) {
        const t = e.target;
        if (t && t.closest && t.closest("[data-rask-reload]")) {
            e.preventDefault();
            location.reload();
        }
    });
})();
