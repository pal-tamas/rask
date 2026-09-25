/** The size of the browser window. */
export interface Viewport {
    width: number;
    height: number;
}

/** Reads the window's size — comes back to C# as a `Viewport` record. */
export function windowSize(): Viewport {
    return { width: window.innerWidth, height: window.innerHeight };
}

/** Half the window, as a tuple — an arrow in a const comes back to C# as `(double Width, double Height)`. */
export const halfSize = (): [width: number, height: number] => [window.innerWidth / 2, window.innerHeight / 2];

/** Counts down once a second, calling back into C# on every tick. */
export class Countdown {
    private timer = 0;

    constructor(private seconds: number) {}

    /** Starts ticking; `onTick` gets the seconds left, `onDone` runs at zero. */
    start(onTick: (left: number) => void, onDone: () => void): void {
        this.stop();
        let left = this.seconds;
        this.timer = window.setInterval(() => {
            left--;
            onTick(left);
            if (left <= 0) {
                this.stop();
                onDone();
            }
        }, 1000);
    }

    stop(): void {
        window.clearInterval(this.timer);
    }
}
