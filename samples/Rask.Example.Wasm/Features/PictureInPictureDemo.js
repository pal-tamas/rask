// Scoped JS for PictureInPictureDemo. Picture-in-Picture needs a *playing* video with real frames, and
// this showcase ships no video file — so we synthesize one: draw an animated canvas and pipe
// canvas.captureStream() into the <video> (resolved from the ElementRef before this runs). Resolves once
// the video is playing, so the C# side can then request the miniplayer.
let started = false;

export function start(video) {
    if (!video || started) {
        return Promise.resolve();
    }
    started = true;

    const canvas = document.createElement("canvas");
    canvas.width = 320;
    canvas.height = 180;
    const ctx = canvas.getContext("2d");
    let t = 0;
    const draw = () => {
        t += 0.03;
        ctx.fillStyle = "#1b1033";
        ctx.fillRect(0, 0, canvas.width, canvas.height);
        const x = (Math.sin(t) * 0.5 + 0.5) * (canvas.width - 60) + 30;
        const y = (Math.cos(t * 0.8) * 0.5 + 0.5) * (canvas.height - 60) + 30;
        ctx.fillStyle = "#7c5cff";
        ctx.beginPath();
        ctx.arc(x, y, 26, 0, Math.PI * 2);
        ctx.fill();
        ctx.fillStyle = "#ffffff";
        ctx.font = "18px sans-serif";
        ctx.fillText("Rask PiP", 14, 28);
        requestAnimationFrame(draw);
    };
    draw();

    video.srcObject = canvas.captureStream(30);
    video.muted = true;
    return video.play();
}
