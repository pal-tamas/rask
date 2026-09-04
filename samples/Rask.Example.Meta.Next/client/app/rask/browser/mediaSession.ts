// Now-playing metadata and hardware media keys — navigator.mediaSession.
//
// See ./geolocation.ts for the two rules every module in this directory follows.
//
// This is what puts your audio on the lock screen, in the notification shade and under the headphone
// button. The browser only surfaces it once something is actually playing.

export interface MediaMetadataInit {
    title?: string | null;
    artist?: string | null;
    album?: string | null;
    /** Cover art. The browser picks the size it wants, so offer more than one where you have them. */
    artwork?: MediaImage[] | null;
}

export function isSupported(): boolean {
    return typeof navigator !== "undefined" && "mediaSession" in navigator;
}

export function setMetadata(metadata: MediaMetadataInit): void {
    navigator.mediaSession.metadata = new MediaMetadata({
        title: metadata.title || "",
        artist: metadata.artist || "",
        album: metadata.album || "",
        artwork: metadata.artwork || []
    });
}

export function setPlaybackState(state: MediaSessionPlaybackState): void {
    navigator.mediaSession.playbackState = state;
}

/**
 * Wire one hardware action — "play", "pause", "nexttrack" and the rest. Passing null clears it.
 *
 * The browser holds exactly one handler per action, so registering a second replaces the first
 * silently; there is no list to append to.
 */
export function setActionHandler(action: MediaSessionAction, handler: (() => void) | null): void {
    navigator.mediaSession.setActionHandler(action, handler);
}

/** Drop the metadata and report nothing playing. */
export function clear(): void {
    navigator.mediaSession.metadata = null;
    navigator.mediaSession.playbackState = "none";
}
