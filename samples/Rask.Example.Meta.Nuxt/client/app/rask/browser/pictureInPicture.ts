// Picture-in-Picture — the PiP API for <video>.
//
// See ./geolocation.ts for the two rules every module in this directory follows.
// Needs transient user activation, like ./fullscreen.ts.

export function isSupported(): boolean {
    return typeof document !== "undefined" && !!document.pictureInPictureEnabled;
}

export function isActive(): boolean {
    return document.pictureInPictureElement != null;
}

/** Pop a video out into the floating miniplayer. */
export function request(video: HTMLVideoElement): Promise<PictureInPictureWindow> {
    return video.requestPictureInPicture();
}

/** Put it back. A no-op when no miniplayer is open. */
export function exit(): Promise<void> {
    return document.pictureInPictureElement ? document.exitPictureInPicture() : Promise.resolve();
}
