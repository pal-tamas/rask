// Camera, microphone and screen capture — navigator.mediaDevices.
//
// See ./geolocation.ts for the two rules every module in this directory follows.
//
// Secure context only, and `getUserMedia` needs transient user activation on most browsers. Stopping
// a stream matters more than usual: until every track is stopped the camera stays on and so does its
// hardware indicator light, which users read — correctly — as being recorded.

export interface DeviceInfo {
    deviceId: string;
    /** "audioinput", "videoinput" or "audiooutput". */
    kind: string;
    /** Empty until the user has granted access to a device of that kind. */
    label: string;
    groupId: string;
}

export interface CaptureConstraints {
    audio?: boolean;
    video?: boolean;
    /** "user" for the selfie camera, "environment" for the rear one. Ignored when video is false. */
    facingMode?: string | null;
}

export function isSupported(): boolean {
    return typeof navigator !== "undefined"
        && !!(navigator.mediaDevices && navigator.mediaDevices.getUserMedia);
}

/**
 * Every device the browser will admit to. Labels are blank until permission has been granted once —
 * enumerate AFTER a successful capture if you want to name the devices in a picker.
 */
export async function enumerate(): Promise<DeviceInfo[]> {
    const devices = await navigator.mediaDevices.enumerateDevices();
    return devices.map((d) => ({deviceId: d.deviceId, kind: d.kind, label: d.label, groupId: d.groupId}));
}

/** Open the camera and/or microphone. Prompts the user the first time. */
export function getUserMedia(constraints: CaptureConstraints): Promise<MediaStream> {
    const video = constraints.video
        ? (constraints.facingMode ? {facingMode: constraints.facingMode} : true)
        : false;
    return navigator.mediaDevices.getUserMedia({audio: !!constraints.audio, video});
}

/** Ask the user to share a screen, window or tab. */
export function getDisplayMedia(): Promise<MediaStream> {
    return navigator.mediaDevices.getDisplayMedia({video: true});
}

/**
 * Show a stream in a video element and start it.
 *
 * Muted, because an unmuted autoplaying video is blocked by every browser's autoplay policy — for a
 * camera preview that is what you want anyway, since the alternative is feeding the microphone back
 * through the speakers.
 */
export function attach(video: HTMLVideoElement, stream: MediaStream): Promise<void> {
    video.srcObject = stream;
    video.muted = true;
    return video.play();
}

/** Stop every track, releasing the hardware and turning the indicator off. */
export function stop(stream: MediaStream): void {
    stream.getTracks().forEach((t) => t.stop());
}
