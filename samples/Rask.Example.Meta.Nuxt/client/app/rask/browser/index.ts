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

export * as auth from "./auth.js";
export * as badge from "./badge.js";
export * as battery from "./battery.js";
export * as broadcastChannel from "./broadcastChannel.js";
export * as cookies from "./cookies.js";
export * as crypto from "./crypto.js";
export * as deviceMotion from "./deviceMotion.js";
export * as deviceOrientation from "./deviceOrientation.js";
export * as eyeDropper from "./eyeDropper.js";
export * as fileSystem from "./fileSystem.js";
export * as fullscreen from "./fullscreen.js";
export * as gamepad from "./gamepad.js";
export * as geolocation from "./geolocation.js";
export * as indexedDb from "./indexedDb.js";
export * as installPrompt from "./installPrompt.js";
export * as intersectionObserver from "./intersectionObserver.js";
export * as mediaDevices from "./mediaDevices.js";
export * as mediaQuery from "./mediaQuery.js";
export * as mediaSession from "./mediaSession.js";
export * as mutationObserver from "./mutationObserver.js";
export * as networkInformation from "./networkInformation.js";
export * as notifications from "./notifications.js";
export * as originPrivateFileSystem from "./originPrivateFileSystem.js";
export * as performance from "./performance.js";
export * as permissions from "./permissions.js";
export * as pictureInPicture from "./pictureInPicture.js";
export * as resizeObserver from "./resizeObserver.js";
export * as screen from "./screen.js";
export * as screenOrientation from "./screenOrientation.js";
export * as signaling from "./signaling.js";
export * as speechRecognition from "./speechRecognition.js";
export * as speechSynthesis from "./speechSynthesis.js";
export * as storageManager from "./storageManager.js";
export * as visualViewport from "./visualViewport.js";
export * as wakeLock from "./wakeLock.js";
export * as webAuthn from "./webAuthn.js";
export * as webLocks from "./webLocks.js";
export * as webPush from "./webPush.js";
