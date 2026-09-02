// Rask's browser layer, as ordinary TypeScript.
//
// Import from here to get everything under a namespace per API:
//
//     import { geolocation, mediaQuery } from "./rask/browser";
//     const fix = await geolocation.getCurrentPosition({enableHighAccuracy: true});
//
// or import a single module directly, which is what a bundler tree-shakes best:
//
//     import { prefersDark } from "./rask/browser/mediaQuery";
//
// Nothing here touches `window` at import time, so these modules are safe to load in a server render
// (Next, Nuxt, SvelteKit) and to call once you are in the browser. The one module that DOES have a
// side effect — ./globals.ts, which publishes the `window.__rask*` namespaces Rask's own C# wrappers
// resolve against — is deliberately not re-exported here: a front end never needs it.

export * as cookies from "./cookies.js";
export * as geolocation from "./geolocation.js";
export * as mediaQuery from "./mediaQuery.js";
export * as networkInformation from "./networkInformation.js";
export * as permissions from "./permissions.js";
export * as screen from "./screen.js";
export * as speechSynthesis from "./speechSynthesis.js";
export * as storageManager from "./storageManager.js";
export * as visualViewport from "./visualViewport.js";
