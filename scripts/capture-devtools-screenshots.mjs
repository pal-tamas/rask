// Drives the real devtools against tests/Rask.DevTools.Showcase and saves the guide's screenshots as PNG.
// Run through scripts/capture-devtools-screenshots.sh, which starts the app and converts the files to WebP.
//
//   node capture-devtools-screenshots.mjs <app url> <out dir> <playwright package dir>
//
// Every step waits on what the reader of the guide will see — the pill, a tab, a row — rather than on time, and
// fails loudly when it does not appear: a screenshot of a panel that never loaded is worse than none. A failed run
// leaves failure.png beside the others, which the shell script keeps when RASK_CAPTURE_KEEP names a folder.
import path from "node:path";
import {createRequire} from "node:module";

const [url, outDir, playwrightDir] = process.argv.slice(2);
const {chromium} = createRequire(import.meta.url)(playwrightDir);

const browser = await chromium.launch();
const context = await browser.newContext({
    viewport: {width: 1280, height: 1100},
    deviceScaleFactor: 2,
    colorScheme: "light",
});
const page = await context.newPage();
const shot = name => page.screenshot({path: path.join(outDir, `${name}.png`)});

try {
    // The pill alone, on a window no taller than the page, so it is not a speck under a screen of nothing.
    await page.setViewportSize({width: 1280, height: 640});
    await page.goto(url);
    const pill = page.locator("rask-devtools .pill");
    await pill.waitFor({timeout: 30000});
    await shot("pill");
    await page.setViewportSize({width: 1280, height: 1100});

    // Open the drawer at the bottom, and give the Wire tab something to show: two clicks in the app.
    await page.evaluate(() => localStorage.setItem("rask.devtools.dock", "bottom"));
    await pill.click();
    const panel = page.frameLocator("rask-devtools iframe");
    await panel.getByText(/Frames received|No traffic yet/).first().waitFor({timeout: 30000});
    const addTask = page.getByRole("button", {name: "Add task"});
    await addTask.click();
    await panel.getByText(/Frames sent\s*1\b/).first().waitFor({timeout: 10000});
    await addTask.click();
    await panel.getByText(/Frames sent\s*2\b/).first().waitFor({timeout: 10000});
    await shot("wire");

    // The Tree tab, opened down to the board's rows on its own. Type-ahead moves the cursor to the deploy card and scrolls it
    // into view: its row is the one with the redacted token.
    await panel.getByRole("tab", {name: "Tree"}).click();
    const tree = panel.getByRole("tree");
    await tree.waitFor({timeout: 10000});
    const row = tree.locator('[role="treeitem"]', {hasText: "TaskRow"}).first();
    await row.waitFor({timeout: 10000});
    await row.locator(".ui-tree-row").first().click();
    await page.keyboard.type("DeployCard");
    await panel.getByText("ApiToken=••••").first().waitFor({timeout: 10000});
    await panel.locator("[role=tablist]").evaluate(el => el.scrollIntoView({block: "start"}));
    await shot("tree");

    // Pointing at a row: the page shows the box around what that component rendered, with its label.
    const taskRow = tree.locator('[role="treeitem"]', {hasText: "Tag the release"}).first();
    await taskRow.locator(".ui-tree-row").hover();
    await page.waitForFunction(() => {
        const root = document.querySelector("rask-devtools")?.shadowRoot;
        const label = root?.querySelector(".hl-label");
        return !!label && !label.hidden && label.textContent.startsWith("TaskRow");
    }, null, {timeout: 10000});
    await shot("highlight");

    // The Renders tab, counting what the two clicks above rendered.
    await page.mouse.move(0, 0);
    const rendersTab = panel.getByRole("tab", {name: "Renders"});
    await rendersTab.click();
    // Waited on what only this tab shows: the Tree tab names TaskBoard too, and a shot taken then catches the switch.
    await panel.locator('[role=tab][aria-selected="true"]', {hasText: "Renders"}).waitFor({timeout: 10000});
    await panel.getByRole("cell", {name: "TaskBoard"}).first().waitFor({timeout: 10000});
    await panel.locator("body").hover({position: {x: 5, y: 5}});
    await panel.locator("[role=tablist]").evaluate(el => el.scrollIntoView({block: "start"}));
    await shot("renders");
} catch (e) {
    await shot("failure").catch(() => {});
    throw e;
} finally {
    await browser.close();
}

