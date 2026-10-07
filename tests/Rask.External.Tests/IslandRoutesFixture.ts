// Node-driven fixture for `@rask/routes` — the module the build generates from this project's own
// [Route] pages (IslandRoutesTests.cs declares them). It is imported exactly as an island imports it,
// against a stub `location`, <base> and host bridge, and prints what it formatted and what it asked the
// host to do. The C# test compares every URL with the one the C# `Routes` class formats.

import { Routes, Go } from '@rask/routes'

type Call = { url: string; replace: boolean }

const env = globalThis as any
const calls: Call[] = []
const errors: string[] = []
let baseHref: string | null = null

env.location = { href: 'http://app.test/app/list?page=1&Sort=asc', pathname: '/app/list', search: '?page=1&Sort=asc' }
env.document = { querySelector: () => (baseHref === null ? null : { href: baseHref }) }
console.error = (message: string) => { errors.push(message) }

// Navigation happens after the caller's own code, so each step is followed by a turn of the microtask queue.
async function after(act: () => void): Promise<Call | undefined> {
    calls.length = 0
    act()
    await Promise.resolve()
    return calls[0]
}

// ----- before the host has booted: nothing to call, and it has to say so -----
await after(() => Routes.LoginPage().Go())
await after(() => Go.With('page', '2'))
const early = [...errors]

env.__raskHost = { navigate: (url: string, replace?: boolean) => { calls.push({ url, replace: replace === true }) } }

// ----- the parity table: the same pages and the same values IslandRoutesTests formats in C# -----
const order = '0b6f3a52-9d1c-4a7e-8f20-3c5d7e9a1b42'
const cases = {
    'front': Routes.FrontPage(),
    'int': Routes.UserPage({ Id: 42 }),
    'negative int': Routes.UserPage({ Id: -7 }),
    'query text': Routes.UserPage({ Id: 42, Tab: 'billing & invoices' }),
    'query left out': Routes.UserPage({ Id: 42, Tab: null }),
    'text needing encoding': Routes.FilePage({ Name: 'a b/c?d#e&f=g+h' }),
    'text encodeURIComponent lets through': Routes.FilePage({ Name: "it's (a) *test*!~" }),
    'non-ascii text': Routes.FilePage({ Name: 'árvíztűrő 日本 😀' }),
    'optional segment': Routes.FilePage({ Name: 'report', Version: 'v2' }),
    'optional segment left out': Routes.FilePage({ Name: 'report' }),
    'guid': Routes.OrderPage({ Id: order }),
    'bool true': Routes.OrderPage({ Id: order, Paid: true }),
    'bool false': Routes.OrderPage({ Id: order, Paid: false }),
    'text under an encoded key': Routes.OrderPage({ Id: order, Order: 'oldest first' }),
    'non-nullable query is always written': Routes.OrderPage({ Id: order, Page: 3 }),
    'every query at once': Routes.OrderPage({ Id: order, Paid: true, Order: 'newest', Page: 2 }),
    'date': Routes.DayPage({ Day: '2026-10-06' }),
    'time': Routes.DayPage({ Day: '2026-01-09', At: '13:05:09' }),
    'date and time': Routes.DayPage({ Day: '2026-01-09', Since: new Date(2026, 2, 4, 5, 6, 7) }),
    'fraction': Routes.DayPage({ Day: '2026-01-09', Ratio: 0.25 }),
    'long': Routes.DayPage({ Day: '2026-01-09', Count: 9007199254740991 }),
    'nested on a name clash': Routes.Admin.HomePage(),
    'nested on a name clash, the other': Routes.Shop.HomePage(),
}

const urls: Record<string, string> = {}
const navigated: Record<string, string | undefined> = {}
for (const [name, route] of Object.entries(cases)) {
    urls[name] = route.Url
    navigated[name] = (await after(() => route.Go()))?.url
}

const history = {
    go: await after(() => Routes.LoginPage().Go()),
    replacing: await after(() => Routes.LoginPage().Go().Replacing()),
    goTo: await after(() => Go.To(Routes.UserPage({ Id: 42 }))),
    goToReplacing: await after(() => Go.To(Routes.UserPage({ Id: 42 })).Replacing()),
}

// ----- this page, with its query changed: /app/list?page=1&Sort=asc -----
const query = {
    with: await after(() => Go.With('sort', 'desc')),
    withNew: await after(() => Go.With('q', 'a b&c')),
    withNull: await after(() => Go.With('PAGE', null)),
    without: await after(() => Go.Without('SORT')),
    withoutMissing: await after(() => Go.Without('nope')),
    withoutAll: await after(() => Go.Without()),
}

// ----- under a path base: <base href="/docs/"> -----
baseHref = 'http://app.test/docs/'
const user = Routes.UserPage({ Id: 42, Tab: 'a b' })
const based = { url: user.Url, link: user.Link, front: Routes.FrontPage().Url }

console.log(JSON.stringify({ urls, navigated, history, query, based, early }))
