// Module-scoped counter — survives across invocations because the IIFE wrapper
// runs once and the closure retains state.
let count = 0;

export function bump() {
    count += 1;
    return count;
}
